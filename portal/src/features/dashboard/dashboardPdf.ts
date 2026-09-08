import { jsPDF } from 'jspdf';
import autoTable from 'jspdf-autotable';
import type { State, Task } from '../../domain/models';
import { isActivitiesProject } from '../../domain/projectRules';
import { translate, type Locale } from '../../shared/i18n';

export function exportDashboardPdf(
  data: State,
  tasks: Task[],
  title: string,
  locale: Locale,
  filters: string,
  projectId?: string,
) {
  const t = (value: string) => translate(locale, value);
  const pdf = new jsPDF({ unit: 'mm', format: 'a4' });
  const now = new Date();
  const today = `${now.getFullYear()}-${String(now.getMonth() + 1).padStart(2, '0')}-${String(now.getDate()).padStart(2, '0')}`;
  const done = (task: Task) => !!data.statuses.find((s) => s.name === task.status)?.complete;
  const open = tasks.filter((task) => !done(task));
  const completed = tasks.length - open.length;
  const scope = projectId ? data.projects.find((p) => p.id === projectId) : undefined;
  const name = scope
    ? isActivitiesProject(scope)
      ? t('My activities')
      : scope.name
    : data.workspace.name;
  const text = (value: string, x: number, y: number, size = 10, color = '#24374f') => {
    pdf.setFontSize(size);
    pdf.setTextColor(color);
    pdf.text(value, x, y);
  };
  pdf.setFillColor('#20344f');
  pdf.rect(0, 0, 210, 65, 'F');
  text('kanbada / ' + t('Dashboard report'), 16, 16, 10, '#b4cce9');
  pdf.setFont('helvetica', 'bold');
  const heading = pdf.splitTextToSize(name, 176);
  text(heading.slice(0, 2), 16, 29, 23, '#ffffff');
  pdf.setFont('helvetica', 'normal');
  text(t(title), 16, 51, 10, '#d3e1f2');
  text(t('Generated on') + ' ' + now.toLocaleString(locale), 16, 59, 8, '#d3e1f2');
  const filterLines = pdf.splitTextToSize(t('Filters') + ': ' + filters, 178);
  text(filterLines, 16, 74, 9);
  let y = 79 + filterLines.length * 4;
  const metrics = [
    ['Total cards', tasks.length],
    ['Completed', completed],
    ['Open cards', open.length],
    ['Overdue', open.filter((task) => !!task.due && task.due < today).length],
    ['High priority', open.filter((task) => task.priority === 'High').length],
    ['Unassigned', open.filter((task) => !task.assignees.length).length],
  ] as const;
  metrics.forEach(([label, value], i) => {
    const x = 16 + (i % 3) * 61;
    const top = y + Math.floor(i / 3) * 29;
    pdf.setFillColor('#eff4fa');
    pdf.roundedRect(x, top, 56, 25, 3, 3, 'F');
    pdf.setFont('helvetica', 'bold');
    text(String(value), x + 5, top + 12, 20);
    pdf.setFont('helvetica', 'normal');
    text(t(label), x + 5, top + 20, 9);
  });
  y += 68;
  text(t('Completion'), 16, y, 12);
  text(`${tasks.length ? Math.round((completed / tasks.length) * 100) : 0}%`, 177, y, 12);
  pdf.setFillColor('#e8eef6');
  pdf.roundedRect(16, y + 5, 178, 4, 2, 2, 'F');
  if (completed) {
    pdf.setFillColor('#7494ba');
    pdf.rect(16, y + 5, (178 * completed) / tasks.length, 4, 'F');
  }
  y += 22;
  text(t('The week ahead'), 16, y, 12);
  y += 8;
  const days = Array.from({ length: 7 }, (_, i) => {
    const date = new Date(now);
    date.setDate(date.getDate() + i);
    const key = `${date.getFullYear()}-${String(date.getMonth() + 1).padStart(2, '0')}-${String(date.getDate()).padStart(2, '0')}`;
    return { date, cards: tasks.filter((task) => task.due === key) };
  });
  const max = Math.max(1, ...days.map((day) => day.cards.length));
  days.forEach((day, i) => {
    const x = 19 + i * 25;
    const h = (day.cards.length / max) * 25;
    pdf.setFillColor('#e8eef6');
    pdf.rect(x, y, 15, 25, 'F');
    if (h) {
      pdf.setFillColor('#7494ba');
      pdf.rect(x, y + 25 - h, 15, h, 'F');
      const completeH = (day.cards.filter(done).length / max) * 25;
      if (completeH) {
        pdf.setFillColor('#29486e');
        pdf.rect(x, y + 25 - completeH, 15, completeH, 'F');
      }
    }
    text(String(day.cards.length), x + 5, y - 2, 9);
    text(day.date.toLocaleDateString(locale, { day: 'numeric', month: 'short' }), x - 1, y + 31, 8);
  });
  text(t('Open') + ' / ' + t('Completed'), 16, y + 39, 8, '#60748e');
  y += 50;
  const table = (heading: string, head: string[], rows: (string | number)[][]) => {
    const estimatedHeight =
      15 +
      rows.reduce(
        (height, row) =>
          height +
          Math.max(
            ...row.map((cell) => pdf.splitTextToSize(String(cell), 160 / head.length).length),
          ) *
            5 +
          6,
        0,
      );
    if (y > 235 || (estimatedHeight < 230 && y + estimatedHeight > 272)) {
      pdf.addPage();
      y = 23;
    }
    pdf.setFont('helvetica', 'bold');
    text(t(heading), 16, y, 13);
    pdf.setFont('helvetica', 'normal');
    autoTable(pdf, {
      startY: y + 5,
      head: [head.map(t)],
      body: rows,
      margin: { left: 16, right: 16, top: 22, bottom: 22 },
      styles: {
        font: 'helvetica',
        fontSize: 9,
        cellPadding: 3,
        textColor: '#24374f',
        lineColor: '#e7edf5',
        overflow: 'linebreak',
      },
      headStyles: { fillColor: '#29486e', textColor: '#ffffff' },
      alternateRowStyles: { fillColor: '#f2f6fb' },
      rowPageBreak: 'avoid',
    });
    y = (pdf as jsPDF & { lastAutoTable: { finalY: number } }).lastAutoTable.finalY + 13;
  };
  table(
    'Workflow at a glance',
    ['Status', 'Total'],
    data.statuses.map((s) => [t(s.name), tasks.filter((task) => task.status === s.name).length]),
  );
  table(
    'Team workload',
    ['Members', 'Open cards'],
    data.members.map((m) => [
      m.name,
      open.filter((task) => task.assignees.includes(m.name)).length,
    ]),
  );
  const counts = (label: string, items: Task[]) => [
    label,
    items.length,
    items.filter((task) => !done(task)).length,
    items.filter(done).length,
    items.filter((task) => !done(task) && !!task.due && task.due < today).length,
  ];
  table(
    'Bucket performance',
    ['Bucket', 'Total', 'Open', 'Done', 'Late'],
    [
      ...data.buckets.map((b) =>
        counts(
          b.name,
          tasks.filter((task) => task.bucket === b.name),
        ),
      ),
      counts(
        t('No bucket'),
        tasks.filter((task) => !task.bucket),
      ),
    ],
  );
  const lanes = data.swimlanes.filter((l) =>
    projectId
      ? l.project === projectId
      : data.projects.some((p) => p.id === l.project && !p.archived),
  );
  table(
    'Swimlane performance',
    ['Swimlane', 'Total', 'Open', 'Done', 'Late'],
    [
      ...lanes.map((l) => {
        const p = data.projects.find((p) => p.id === l.project);
        return counts(
          l.name +
            (projectId
              ? ''
              : ' / ' + (p && isActivitiesProject(p) ? t('My activities') : (p?.name ?? ''))),
          tasks.filter((task) => task.swimlane === l.name && task.project === l.project),
        );
      }),
      counts(
        t('No swimlane'),
        tasks.filter((task) => !task.swimlane),
      ),
    ],
  );
  const checklist = tasks.flatMap((task) => task.checklist);
  table(
    'Checklist progress',
    ['Completed', 'Total'],
    [[checklist.filter((item) => item.done).length, checklist.length]],
  );
  const recent = tasks
    .flatMap((task) => (task.history ?? []).map((entry) => ({ task, entry })))
    .sort((a, b) => b.entry.at.localeCompare(a.entry.at))
    .slice(0, 5);
  if (recent.length)
    table(
      'Latest movement',
      ['Task name', 'Card history', 'Members'],
      recent.map(({ task, entry }) => [
        task.title,
        t(entry.changes[0]) + ' / ' + new Date(entry.at).toLocaleDateString(locale),
        entry.actor,
      ]),
    );
  const note =
    t(
      'Charts and metrics follow the dashboard’s bucket and swimlane filters. Overdue means an unfinished card due before today. Workspace metrics exclude archived projects.',
    ) +
    ' ' +
    t('Shared cards count once for each assigned teammate.');
  pdf.setFontSize(8);
  const lines = pdf.splitTextToSize(note, 178);
  if (y + lines.length * 4 > 272) {
    pdf.addPage();
    y = 23;
  }
  text(lines, 16, y, 8, '#60748e');
  const pages = pdf.getNumberOfPages();
  for (let i = 1; i <= pages; i++) {
    pdf.setPage(i);
    pdf.setDrawColor('#dbe4ef');
    pdf.line(16, 279, 194, 279);
    text('KANBADA', 16, 286, 8, '#60748e');
    text(`${t('Page')} ${i} / ${pages}`, 169, 286, 8, '#60748e');
    if (i > 1) text(pdf.splitTextToSize(name, 176)[0], 16, 13, 9, '#60748e');
  }
  pdf.save(`${name.replace(/[^\p{L}\p{N} -]/gu, '').slice(0, 70) || 'kanbada'}-${today}.pdf`);
}
