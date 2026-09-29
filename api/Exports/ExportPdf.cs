using System.Globalization;
using System.Text.Json.Nodes;
using PdfSharp.Drawing;
using PdfSharp.Fonts;
using PdfSharp.Pdf;

namespace Kanbada.Api;

public static class ExportPdf
{
    private sealed class Fonts : IFontResolver
    {
        private readonly Lazy<byte[]> font = new(() =>
        {
            var configured = Environment.GetEnvironmentVariable("EXPORT_PDF_FONT");
            var path = configured ?? new[]
            {
                "/usr/share/fonts/truetype/dejavu/DejaVuSans.ttf",
                "/System/Library/Fonts/Supplemental/Arial.ttf",
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Fonts), "arial.ttf")
            }.FirstOrDefault(File.Exists);
            if (path is null || !File.Exists(path)) throw new InvalidOperationException("Install DejaVu Sans or configure EXPORT_PDF_FONT with a Unicode TrueType font.");
            return File.ReadAllBytes(path);
        });
        public byte[] GetFont(string faceName) => font.Value;
        public FontResolverInfo ResolveTypeface(string familyName, bool bold, bool italic) => new("export", bold, italic);
    }

    static ExportPdf() => GlobalFontSettings.FontResolver = new Fonts();

    public static void Render(Stream output, ExportJobEntity job, CardQuery query, JsonObject state, JsonObject summary, DateTimeOffset generatedAt)
    {
        var culture = CultureInfo.GetCultureInfo(job.Locale);
        string T(string text) => job.Locale == "pt-PT" ? Portuguese.GetValueOrDefault(text, text) : text;
        string Text(JsonNode? node, string key) => WorkspaceJson.Text(node, key);
        int Number(JsonNode? node, string key) => node?[key]?.GetValue<int>() ?? 0;
        JsonArray Items(JsonNode? node, string key) => WorkspaceJson.Items(node, key);
        var counts = summary["counts"];
        var groups = Items(summary, "groups");
        var total = Number(counts, "total");
        var complete = Number(counts, "completed");
        var today = query.Today ?? DateOnly.FromDateTime(generatedAt.UtcDateTime);
        using var pdf = new PdfDocument();
        pdf.Info.Title = job.Name + " / " + T("Dashboard report");
        pdf.Info.Author = "Kanbada";
        var regular = new XFont("Export", 9, XFontStyleEx.Regular);
        var bold = new XFont("Export", 12, XFontStyleEx.Bold);
        var ink = new XSolidBrush(XColor.FromArgb(36, 55, 79));
        var blue = new XSolidBrush(XColor.FromArgb(116, 148, 186));
        var pale = new XSolidBrush(XColor.FromArgb(239, 244, 250));
        const double left = 44, width = 507, bottom = 780;
        XGraphics? graphics = null;
        double y = 0;
        void NewPage()
        {
            graphics?.Dispose();
            var page = pdf.AddPage();
            page.Size = PdfSharp.PageSize.A4;
            graphics = XGraphics.FromPdfPage(page);
            y = 42;
        }
        NewPage();
        List<string> Wrap(string value, XFont font, double available)
        {
            var lines = new List<string>();
            foreach (var paragraph in value.Replace("\r", "").Split('\n'))
            {
                var line = "";
                foreach (var rune in paragraph.EnumerateRunes())
                {
                    var next = line + rune;
                    if (line.Length > 0 && graphics!.MeasureString(next, font).Width > available)
                    {
                        lines.Add(line);
                        line = rune.ToString();
                    }
                    else line = next;
                }
                lines.Add(line);
            }
            return lines;
        }
        void Paragraph(string value, bool heading = false)
        {
            var font = heading ? bold : regular;
            foreach (var line in Wrap(value, font, width))
            {
                if (y + 18 > bottom) NewPage();
                graphics!.DrawString(line, font, ink, left, y + 12);
                y += heading ? 19 : 14;
            }
            y += 8;
        }
        void Table(string title, string[] headers, IEnumerable<string[]> values)
        {
            if (y + 60 > bottom) NewPage();
            Paragraph(T(title), true);
            var cellWidth = width / headers.Length;
            void Header()
            {
                if (y + 28 > bottom) NewPage();
                graphics!.DrawRectangle(blue, left, y, width, 23);
                for (var i = 0; i < headers.Length; i++)
                    graphics.DrawString(T(headers[i]), regular, XBrushes.White, left + i * cellWidth + 5, y + 15);
                y += 25;
            }
            Header();
            foreach (var row in values)
            {
                var lines = row.Select(cell => Wrap(cell, regular, cellWidth - 10)).ToArray();
                var remaining = lines.Max(cell => cell.Count);
                var offset = 0;
                while (remaining > 0)
                {
                    if (y + 23 > bottom) { NewPage(); Header(); }
                    var count = Math.Min(remaining, Math.Max(1, (int)((bottom - y - 8) / 13)));
                    graphics!.DrawRectangle(pale, left, y, width, count * 13 + 8);
                    for (var col = 0; col < lines.Length; col++)
                        for (var line = 0; line < count && offset + line < lines[col].Count; line++)
                            graphics.DrawString(lines[col][offset + line], regular, ink, left + col * cellWidth + 5, y + 13 + line * 13);
                    y += count * 13 + 10;
                    offset += count;
                    remaining -= count;
                }
            }
            y += 12;
        }
        string N(int value) => value.ToString(culture);
        Paragraph("KANBADA / " + T("Dashboard report"), true);
        Paragraph(job.Name, true);
        Paragraph(T("Generated on") + " " + generatedAt.ToString("g", culture) + " UTC");
        var chosenBucket = query.Bucket is null ? T("All buckets") : query.Bucket == "" ? T("No bucket") : query.Bucket;
        var chosenLane = query.Swimlane is null ? T("All swimlanes") : query.Swimlane == "" ? T("No swimlane")
            : Text(Items(state, "swimlanes").FirstOrDefault(l => Text(l, "id") == query.Swimlane), "name");
        Paragraph(T("Filters") + ": " + T("Bucket") + " = " + chosenBucket + "; " + T("Swimlane") + " = " + chosenLane);
        foreach (var (label, value) in new[] { ("Search", query.Search), ("Priority", query.Priority), ("Assignee", query.Person),
            ("Status", query.Status), ("Completion", query.Completion), ("From", query.From?.ToString("yyyy-MM-dd")), ("To", query.To?.ToString("yyyy-MM-dd")) })
            if (value is not null) Paragraph(T(label) + ": " + T(value));
        if (query.Mine == true) Paragraph(T("My tasks"));
        if (query.Unassigned == true) Paragraph(T("Unassigned"));
        Table("Overview", ["Metric", "Total"], new[]
        {
            new[] { T("Total cards"), N(total) }, new[] { T("Completed"), N(complete) },
            new[] { T("Open cards"), N(Number(counts, "open")) }, new[] { T("Overdue"), N(Number(counts, "overdue")) },
            new[] { T("High priority"), N(Number(counts, "highPriority")) }, new[] { T("Unassigned"), N(Number(counts, "unassigned")) }
        });
        Paragraph(T("Completion") + ": " + (total == 0 ? 0 : Math.Round(100d * complete / total)).ToString(culture) + "%", true);
        if (y + 16 > bottom) NewPage();
        graphics!.DrawRectangle(pale, left, y, width, 10);
        if (total > 0 && complete > 0) graphics.DrawRectangle(blue, left, y, width * complete / total, 10);
        y += 30;
        if (y + 125 > bottom) NewPage();
        Paragraph(T("The week ahead"), true);
        var dates = Enumerable.Range(0, 7).Select(i =>
        {
            var date = today.AddDays(i);
            var values = Items(summary, "due").FirstOrDefault(d => Text(d, "date") == date.ToString("yyyy-MM-dd"));
            return (Date: date, Total: Number(values, "total"), Done: Number(values, "completed"));
        }).ToArray();
        var max = Math.Max(1, dates.Max(d => d.Total));
        for (var i = 0; i < dates.Length; i++)
        {
            var day = dates[i];
            var x = left + i * 72;
            graphics.DrawRectangle(pale, x, y, 48, 65);
            if (day.Total > 0)
            {
                graphics.DrawRectangle(blue, x, y + 65 - 65d * day.Total / max, 48, 65d * day.Total / max);
                if (day.Done > 0) graphics.DrawRectangle(ink, x, y + 65 - 65d * day.Done / max, 48, 65d * day.Done / max);
            }
            graphics.DrawString(N(day.Total), regular, ink, x + 4, y - 4);
            graphics.DrawString(day.Date.ToString("dd MMM", culture), regular, ink, x, y + 79);
        }
        y += 101;
        Paragraph(T("Open") + " / " + T("Completed"));
        Table("Workflow at a glance", ["Status", "Total"], Items(state, "statuses").Select(s =>
            new[] { T(Text(s, "name")), N(groups.Where(g => Text(g, "status") == Text(s, "id")).Sum(g => Number(g, "total"))) }));
        Table("Team workload", ["Members", "Open cards"], Items(state, "members").Select(m =>
            new[] { Text(m, "name"), N(Items(summary, "workload").Where(w => Text(w, "email") == Text(m, "email")).Sum(w => Number(w, "total"))) }));
        string[] GroupRow(string name, IEnumerable<JsonNode?> rows)
        {
            var entries = rows.ToArray();
            var all = entries.Sum(g => Number(g, "total"));
            var done = entries.Sum(g => Number(g, "completed"));
            return [name, N(all), N(all - done), N(done), N(entries.Sum(g => Number(g, "overdue")))];
        }
        Table("Bucket performance", ["Bucket", "Total", "Open", "Done", "Late"],
            Items(state, "buckets").Select(b => GroupRow(Text(b, "name"), groups.Where(g => Text(g, "bucket") == Text(b, "id"))))
                .Append(GroupRow(T("No bucket"), groups.Where(g => Text(g, "bucket") == ""))));
        var projects = Items(state, "projects");
        Table("Swimlane performance", ["Swimlane", "Total", "Open", "Done", "Late"],
            Items(state, "swimlanes").Where(l => query.Project is not null ? Text(l, "project") == query.Project
                : projects.Any(p => Text(p, "id") == Text(l, "project") && p?["archived"]?.GetValue<bool>() != true))
                .Select(l => GroupRow(Text(l, "name") + (query.Project is null ? " / " + T(Text(projects.FirstOrDefault(p => Text(p, "id") == Text(l, "project")), "name")) : ""),
                    groups.Where(g => Text(g, "swimlane") == Text(l, "id"))))
                .Append(GroupRow(T("No swimlane"), groups.Where(g => Text(g, "swimlane") == ""))));
        Table("Checklist progress", ["Completed", "Total"], [new[] { N(Number(summary["checklist"], "completed")), N(Number(summary["checklist"], "total")) }]);
        Table("Latest movement", ["Task name", "Card history", "Members"], Items(summary, "activity").Select(e =>
            new[] { Text(e, "title"), T(Text(e, "change")) + " / " + DateTimeOffset.Parse(Text(e, "at")).ToString("d", culture), Text(e, "actor") }));
        Paragraph(T("Shared cards count once for each assigned teammate."));
        Paragraph(T("Overdue means an unfinished card due before today.") + " " + today.ToString("d", culture));
        graphics.Dispose();
        for (var i = 0; i < pdf.PageCount; i++)
        {
            using var footer = XGraphics.FromPdfPage(pdf.Pages[i], XGraphicsPdfPageOptions.Append);
            footer.DrawString("KANBADA", regular, ink, left, 817);
            footer.DrawString(T("Page") + $" {i + 1} / {pdf.PageCount}", regular, ink, 450, 817);
        }
        pdf.Save(output, closeStream: false);
    }

    private static readonly Dictionary<string, string> Portuguese = new()
    {
        ["Dashboard report"] = "Relatório do painel",
        ["Generated on"] = "Gerado em",
        ["Filters"] = "Filtros",
        ["All buckets"] = "Todos os grupos",
        ["No bucket"] = "Sem grupo",
        ["All swimlanes"] = "Todas as faixas",
        ["No swimlane"] = "Sem faixa",
        ["Bucket"] = "Grupo",
        ["Swimlane"] = "Faixa",
        ["Search"] = "Pesquisa",
        ["Priority"] = "Prioridade",
        ["Assignee"] = "Responsável",
        ["Status"] = "Estado",
        ["From"] = "De",
        ["To"] = "Até",
        ["Overview"] = "Visão geral",
        ["Metric"] = "Métrica",
        ["Total"] = "Total",
        ["Total cards"] = "Total de cartões",
        ["Completed"] = "Concluídos",
        ["Open cards"] = "Cartões em aberto",
        ["Overdue"] = "Em atraso",
        ["High priority"] = "Alta prioridade",
        ["Unassigned"] = "Sem responsável",
        ["Completion"] = "Conclusão",
        ["The week ahead"] = "A semana que se segue",
        ["Open"] = "Em aberto",
        ["Done"] = "Concluído",
        ["Late"] = "Atraso",
        ["Workflow at a glance"] = "O processo num relance",
        ["Team workload"] = "Carga de trabalho",
        ["Members"] = "Membros",
        ["Bucket performance"] = "Desempenho por grupo",
        ["Swimlane performance"] = "Desempenho por faixa",
        ["Checklist progress"] = "Progresso da lista de verificação",
        ["Latest movement"] = "Últimas alterações",
        ["Task name"] = "Nome da tarefa",
        ["Card history"] = "Histórico do cartão",
        ["Page"] = "Página",
        ["My tasks"] = "As minhas tarefas",
        ["My activities"] = "As minhas atividades",
        ["Shared cards count once for each assigned teammate."] = "Os cartões partilhados contam uma vez por cada responsável.",
        ["Backlog"] = "Por iniciar",
        ["In progress"] = "Em curso",
        ["Low"] = "Baixa",
        ["Medium"] = "Média",
        ["High"] = "Alta",
        ["Overdue means an unfinished card due before today."] = "Em atraso significa um cartão por concluir com prazo anterior a hoje."
    };
}
