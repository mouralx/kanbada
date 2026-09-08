export type HelpArticle = {
  id: string;
  category: string;
  en: [string, string];
  pt: [string, string];
};
export const helpCategories = [
  ['All topics', 'Todos os temas'],
  ['Account & appearance', 'Conta e aparência'],
  ['Workspaces & members', 'Espaços e membros'],
  ['Projects & personal tasks', 'Projetos e tarefas pessoais'],
  ['Cards & files', 'Cartões e ficheiros'],
  ['Workflow & organization', 'Processo e organização'],
  ['Dashboards & exports', 'Painéis e exportações'],
  ['Links & sharing', 'Ligações e partilha'],
];
export const helpArticles: HelpArticle[] = [
  {
    id: 'signin',
    category: 'Account & appearance',
    en: [
      'How do I sign in or register?',
      'On the sign-in screen, choose Continue with Google or Continue with Microsoft. Follow the provider’s sign-in steps to open your workspace. You can also choose Continue with email to sign in or create an account. Google and Microsoft sign-in opens the provider’s secure sign-in page when enabled.',
    ],
    pt: [
      'Como inicio sessão ou crio uma conta?',
      'No ecrã de início de sessão, escolha Continuar com Google ou Continuar com Microsoft. Siga os passos do fornecedor para abrir o espaço de trabalho. Também pode escolher Continuar com email para iniciar sessão ou criar uma conta. O início de sessão Google ou Microsoft abre a página segura do fornecedor quando está ativado.',
    ],
  },
  {
    id: 'logout',
    category: 'Account & appearance',
    en: [
      'How do I log out?',
      'Choose Sign out beside Settings in the sidebar, or open your profile and choose Sign out. On a small screen, open the navigation menu or use the profile avatar in the header. You return to the sign-in screen; saved work remains available when you sign in again.',
    ],
    pt: [
      'Como termino a sessão?',
      'Escolha Terminar sessão junto de Definições no menu lateral, ou abra o perfil e escolha Terminar sessão. Num ecrã pequeno, abra o menu de navegação ou use o avatar do perfil no cabeçalho. Regressa ao início de sessão; o trabalho guardado continua disponível quando voltar a entrar.',
    ],
  },
  {
    id: 'profile',
    category: 'Account & appearance',
    en: [
      'How do I change my name, email, or profile picture?',
      'Open your avatar in the header or your profile at the bottom of the sidebar. Edit your name or use Change photo. Your sign-in email is read-only. Preview the image, then choose Save profile. JPG, PNG, WebP, and GIF files up to 2 MB are supported. Remove photo restores your initials after saving.',
    ],
    pt: [
      'Como altero o nome, o email ou a fotografia de perfil?',
      'Abra o avatar no cabeçalho ou o perfil no fundo do menu lateral. Edite o nome ou use Alterar fotografia. O email de início de sessão é apenas de leitura. Veja a pré-visualização e escolha Guardar perfil. São aceites imagens JPG, PNG, WebP e GIF até 2 MB. Remover fotografia repõe as iniciais depois de guardar.',
    ],
  },
  {
    id: 'theme',
    category: 'Account & appearance',
    en: [
      'How do I switch between Light, Dark, and System?',
      'Use the theme selector in the header, in Settings, or on the sign-in screen. Light and Dark stay fixed. System follows your device appearance, including changes while Kanbada is open. Your choice is remembered across reloads.',
    ],
    pt: [
      'Como alterno entre Claro, Escuro e Sistema?',
      'Use o seletor de tema no cabeçalho, em Definições ou no ecrã de início de sessão. Claro e Escuro mantêm-se fixos. Sistema acompanha a aparência do dispositivo, incluindo alterações enquanto o Kanbada está aberto. A escolha é guardada ao recarregar.',
    ],
  },
  {
    id: 'language',
    category: 'Account & appearance',
    en: [
      'How do I change the language?',
      'Select EN or PT in the header for English (United States) or Portuguese (Portugal). Kanbada remembers the choice. Interface text and dates change language; names and descriptions you write stay as entered.',
    ],
    pt: [
      'Como altero o idioma?',
      'Selecione EN ou PT no cabeçalho para inglês dos Estados Unidos ou português de Portugal. O Kanbada guarda a escolha. Os textos da interface e as datas mudam de idioma; os nomes e as descrições que escreveu mantêm-se.',
    ],
  },
  {
    id: 'navigation',
    category: 'Account & appearance',
    en: [
      'How do I collapse the sidebar or use keyboard shortcuts?',
      'Use the arrow on the edge of the sidebar to collapse it to icons; use it again to expand. On mobile, use the navigation button in the header. Press Ctrl+K or Command+K to focus task search. Escape closes the top dialog or the card panel.',
    ],
    pt: [
      'Como recolho o menu lateral ou uso atalhos de teclado?',
      'Use a seta na margem do menu lateral para o reduzir a ícones; volte a premir para expandir. No telemóvel, use o botão de navegação no cabeçalho. Ctrl+K ou Command+K coloca o foco na pesquisa de tarefas. Escape fecha a caixa de diálogo superior ou o painel do cartão.',
    ],
  },
  {
    id: 'notifications',
    category: 'Account & appearance',
    en: [
      'How do I clear notifications?',
      'Open the bell in the header. Dismiss an individual notification or use Clear notifications to remove them all. The unread indicator clears, but card history and workspace activity are preserved. Later updates can create new notifications.',
    ],
    pt: [
      'Como limpo as notificações?',
      'Abra o sino no cabeçalho. Dispense uma notificação ou use Limpar notificações para remover todas. O indicador de notificações desaparece, mas o histórico dos cartões e a atividade do espaço são preservados. As alterações seguintes podem gerar novas notificações.',
    ],
  },
  {
    id: 'workspaces',
    category: 'Workspaces & members',
    en: [
      'How do I create or switch workspaces?',
      'Click the workspace name in the sidebar. Enter a New workspace name and choose Create workspace, or select an existing workspace. Each workspace has its own projects, cards, members, and workflow settings. New workspaces include My activities.',
    ],
    pt: [
      'Como crio ou mudo de espaço de trabalho?',
      'Clique no nome do espaço no menu lateral. Introduza o nome do novo espaço e escolha Criar espaço de trabalho, ou selecione um espaço existente. Cada espaço tem os seus projetos, cartões, membros e definições de processo. Os novos espaços incluem As minhas atividades.',
    ],
  },
  {
    id: 'workspace-images',
    category: 'Workspaces & members',
    en: [
      'How do I change the workspace icon and banner?',
      'Open Settings → Workspace appearance. Choose Change icon or Change banner, upload a JPG, PNG, or WebP image up to 10 MB, and review the preview. Adjust Banner position if needed, then Save workspace images. Remove icon or Remove banner restores the default after saving.',
    ],
    pt: [
      'Como altero o ícone e a imagem de capa do espaço?',
      'Abra Definições → Aparência do espaço de trabalho. Escolha Alterar ícone ou Alterar imagem de capa, carregue uma imagem JPG, PNG ou WebP até 10 MB e confirme a pré-visualização. Ajuste a posição da imagem de capa se necessário e escolha Guardar imagens do espaço de trabalho. Remover ícone ou Remover imagem de capa repõe a aparência inicial depois de guardar.',
    ],
  },
  {
    id: 'workspace-delete',
    category: 'Workspaces & members',
    en: [
      'How do I delete a workspace? Why is My Workspace protected?',
      'Open the workspace switcher and use the delete icon beside an additional workspace, then confirm. Deletion removes its projects, cards, history, definitions, and unshared files. Deleting the current workspace returns you to My Workspace. My Workspace always exists and cannot be deleted.',
    ],
    pt: [
      'Como elimino um espaço? Porque está My Workspace protegido?',
      'Abra o seletor de espaços e use o ícone de eliminar junto de um espaço adicional; confirme a operação. A eliminação remove os projetos, cartões, histórico, definições e ficheiros não partilhados. Se eliminar o espaço atual, regressa a My Workspace. My Workspace existe sempre e não pode ser eliminado.',
    ],
  },
  {
    id: 'members',
    category: 'Workspaces & members',
    en: [
      'How do I add or remove workspace members?',
      'Use Invite members, enter a name and email, and choose Add teammate. Open Members to see everyone and remove a member with confirmation. Removing someone clears their card assignments while preserving the cards. The workspace owner cannot be removed. Use Copy invitation link on the new member’s card and send it to them. They must sign in with the invited email and accept the invitation to access the workspace.',
    ],
    pt: [
      'Como adiciono ou removo membros do espaço?',
      'Use Adicionar membros, introduza o nome e o email e confirme a adição. Abra Membros para ver a lista e remover uma pessoa mediante confirmação. A remoção limpa as atribuições dessa pessoa e preserva os cartões. O proprietário do espaço não pode ser removido. Use Copiar ligação de convite no cartão do novo membro e envie-a à pessoa. Para aceder ao espaço, tem de iniciar sessão com o email convidado e aceitar o convite.',
    ],
  },
  {
    id: 'storage',
    category: 'Workspaces & members',
    en: [
      'Where is my work saved?',
      'Your work and files are stored by the server in PostgreSQL. Card edits need Save task; settings have their own Save buttons. Sign in on another device to access your workspaces. If someone else changes a workspace before your save, refresh and review the latest version before applying your changes.',
    ],
    pt: [
      'Onde é guardado o meu trabalho?',
      'O trabalho e os ficheiros são guardados pelo servidor em PostgreSQL. As edições dos cartões precisam de Guardar tarefa; as definições têm botões próprios. Inicie sessão noutro dispositivo para aceder aos seus espaços. Se outra pessoa alterar um espaço antes de guardar, atualize e reveja a versão mais recente antes de aplicar as alterações.',
    ],
  },
  {
    id: 'projects',
    category: 'Projects & personal tasks',
    en: [
      'How do I create and open projects?',
      'Choose New project in the sidebar, enter a name, description, and color, and choose Create project. Projects opens the directory with Live and Archived tabs. Select a project there or use its shortcut in the sidebar to open it.',
    ],
    pt: [
      'Como crio e abro projetos?',
      'Escolha Novo projeto no menu lateral, introduza o nome, a descrição e a cor, e escolha Criar projeto. Projetos abre o diretório com os separadores Ativos e Arquivados. Selecione um projeto no diretório ou use o atalho no menu lateral para o abrir.',
    ],
  },
  {
    id: 'personal',
    category: 'Projects & personal tasks',
    en: [
      'What is the difference between My tasks and My activities?',
      'My tasks lists cards assigned to you across all active projects in the current workspace, grouped by project by default. My activities is a permanent project for your everyday work. New cards created from My tasks go into My activities. Archived projects are excluded from My tasks.',
    ],
    pt: [
      'Qual é a diferença entre As minhas tarefas e As minhas atividades?',
      'As minhas tarefas lista os cartões atribuídos a si em todos os projetos ativos do espaço atual, agrupados por projeto por predefinição. As minhas atividades é um projeto permanente para o trabalho do dia a dia. Os novos cartões criados a partir de As minhas tarefas ficam em As minhas atividades. Os projetos arquivados não aparecem em As minhas tarefas.',
    ],
  },
  {
    id: 'archive',
    category: 'Projects & personal tasks',
    en: [
      'How do I archive or restore a project?',
      'Use Archive project in the project options menu or the archive action in Projects. Confirm to move it to Archived while retaining its cards, files, and history. Open Projects → Archived and use Restore project to make it live again. My activities cannot be archived.',
    ],
    pt: [
      'Como arquivo ou restauro um projeto?',
      'Use Arquivar projeto no menu de opções do projeto ou a ação de arquivo em Projetos. Confirme para o mover para Arquivados, preservando cartões, ficheiros e histórico. Abra Projetos → Arquivados e use Restaurar projeto para o tornar ativo novamente. As minhas atividades não pode ser arquivado.',
    ],
  },
  {
    id: 'delete-project',
    category: 'Projects & personal tasks',
    en: [
      'How do I delete a project?',
      'Use Delete project in the project directory or project options menu and confirm. This permanently removes its cards, history, swimlanes, and files not referenced elsewhere. Choose archive instead when you want to keep the project for later. My activities cannot be deleted or renamed.',
    ],
    pt: [
      'Como elimino um projeto?',
      'Use Eliminar projeto no diretório ou no menu de opções do projeto e confirme. A operação remove definitivamente os cartões, histórico, faixas e ficheiros sem outras referências. Escolha arquivar se quiser guardar o projeto para mais tarde. As minhas atividades não pode ser eliminado nem renomeado.',
    ],
  },
  {
    id: 'card-edit',
    category: 'Cards & files',
    en: [
      'How do I create, edit, or save a card?',
      'Open a project and choose Add task, or click an existing card in the board, list, calendar, or dashboard. The right-side panel shows the associated project above the title. Edit the details and choose Save task. Closing the panel without saving discards the pending edits. Card IDs are generated in uppercase.',
    ],
    pt: [
      'Como crio, edito ou guardo um cartão?',
      'Abra um projeto e escolha Adicionar tarefa, ou clique num cartão no quadro, na lista, no calendário ou num painel. O painel lateral direito mostra o projeto associado acima do título. Edite os detalhes e escolha Guardar tarefa. Fechar o painel sem guardar descarta as alterações pendentes. Os identificadores dos cartões são gerados em maiúsculas.',
    ],
  },
  {
    id: 'card-copy-delete',
    category: 'Cards & files',
    en: [
      'How do I duplicate or delete a card?',
      'Open the card and use Duplicate task or Delete task at the bottom of the Details panel. A duplicate gets a new ID and its own history. Delete task removes the card immediately, so use it only when you no longer need the card.',
    ],
    pt: [
      'Como duplico ou elimino um cartão?',
      'Abra o cartão e use Duplicar tarefa ou Eliminar tarefa no fundo do painel Detalhes. A cópia recebe um novo identificador e histórico próprio. Eliminar tarefa remove o cartão imediatamente; use esta ação apenas quando já não precisar dele.',
    ],
  },
  {
    id: 'assignees',
    category: 'Cards & files',
    en: [
      'How do I assign a card to several people?',
      'Open the card, find Assignees, and select each member who should be responsible. Select a checked member again to remove the assignment. A card can have several assignees or none. Choose Save task to apply the changes.',
    ],
    pt: [
      'Como atribuo um cartão a várias pessoas?',
      'Abra o cartão, encontre Responsáveis e selecione cada membro que deve ficar responsável. Volte a selecionar um membro assinalado para retirar a atribuição. Um cartão pode ter vários responsáveis ou nenhum. Escolha Guardar tarefa para aplicar as alterações.',
    ],
  },
  {
    id: 'dates',
    category: 'Cards & files',
    en: [
      'Do cards need a due date? How do I remove one?',
      'Due date is optional. Leave it empty when creating a card, or clear the date in the card panel and save. Undated cards stay on the board and list but do not appear in the calendar or count as overdue. Priority can be Low, Medium, or High independently of the date.',
    ],
    pt: [
      'Os cartões precisam de data limite? Como a removo?',
      'A data limite é opcional. Deixe-a vazia ao criar o cartão ou limpe a data no painel do cartão e guarde. Os cartões sem data continuam no quadro e na lista, mas não aparecem no calendário nem contam como atrasados. A prioridade pode ser Baixa, Média ou Alta, independentemente da data.',
    ],
  },
  {
    id: 'files',
    category: 'Cards & files',
    en: [
      'How do I attach, download, or remove documents?',
      'Open card Details and find the attachments area. Browse or drop files of up to 25 MB each. Choose Save task to keep the attachments. Use the download action to get the original file, or the remove action and then Save task to detach it.',
    ],
    pt: [
      'Como anexo, transfiro ou removo documentos?',
      'Abra Detalhes do cartão e encontre a área de anexos. Selecione ou arraste ficheiros até 25 MB cada. Escolha Guardar tarefa para manter os anexos. Use a ação de transferência para obter o ficheiro original, ou a ação de remover seguida de Guardar tarefa para o desanexar.',
    ],
  },
  {
    id: 'checklist',
    category: 'Cards & files',
    en: [
      'How do I use checklists and comments?',
      'In card Details, type a checklist item and press Enter. Check completed items or remove items you no longer need. Under Conversation, write a comment and use the send arrow. Choose Save task to keep checklist changes and comments.',
    ],
    pt: [
      'Como uso listas de verificação e comentários?',
      'Em Detalhes, escreva um item da lista de verificação e prima Enter. Assinale os itens concluídos ou remova os que já não precisa. Em Conversa, escreva um comentário e use a seta de envio. Escolha Guardar tarefa para conservar as alterações e os comentários.',
    ],
  },
  {
    id: 'history',
    category: 'Cards & files',
    en: [
      'Where can I see a card’s history?',
      'Open the card and select History next to Details. Entries show the actor, time, and saved changes, with the newest first. Status moves, assignments, labels, checklists, comments, files, and other saved edits appear here. Unsaved changes are not history entries.',
    ],
    pt: [
      'Onde vejo o histórico de um cartão?',
      'Abra o cartão e selecione Histórico junto de Detalhes. As entradas mostram o autor, a data e as alterações guardadas, começando pelas mais recentes. Movimentos de estado, atribuições, etiquetas, listas de verificação, comentários, ficheiros e outras edições guardadas aparecem aqui. Alterações por guardar não são entradas do histórico.',
    ],
  },
  {
    id: 'views',
    category: 'Workflow & organization',
    en: [
      'How do Board, List, and Calendar differ?',
      'Board arranges cards in columns and swimlanes. List provides a compact view that can group cards by project, bucket, or swimlane, with counts for each group. Calendar places dated cards on their due dates; use its arrows to change month. Use the view tabs to switch.',
    ],
    pt: [
      'Qual é a diferença entre Quadro, Lista e Calendário?',
      'Quadro organiza os cartões em colunas e faixas. Lista oferece uma vista compacta que pode agrupar por projeto, grupo ou faixa, com contagens em cada conjunto. Calendário coloca os cartões com data nos respetivos dias; use as setas para mudar de mês. Use os separadores para alternar entre vistas.',
    ],
  },
  {
    id: 'statuses',
    category: 'Workflow & organization',
    en: [
      'How do I define statuses and mark work as completed?',
      'Open a project and choose Manage statuses. Add, rename, color, and reorder statuses, then save. Mark the statuses that represent completed work: dashboards use this setting to decide which cards are done. Move a card to a status by dragging it on a status-grouped board or editing its Status. At least one status must remain.',
    ],
    pt: [
      'Como defino estados e marco trabalho como concluído?',
      'Abra um projeto e escolha Gerir estados. Adicione, renomeie, altere cores e ordene os estados; depois guarde. Assinale os estados que representam trabalho concluído: os painéis usam essa definição para contar os cartões concluídos. Mova um cartão arrastando-o num quadro agrupado por estado ou editando o respetivo Estado. Tem de existir pelo menos um estado.',
    ],
  },
  {
    id: 'buckets',
    category: 'Workflow & organization',
    en: [
      'How do buckets work?',
      'Choose Manage buckets in a project to add, rename, color, or reorder buckets. Buckets are shared across the workspace and are independent of status. Set a card’s bucket in its details, or group the board by Bucket and drag the card to a column. An in-use bucket must be cleared from its cards before it can be deleted.',
    ],
    pt: [
      'Como funcionam os grupos?',
      'Escolha Gerir grupos num projeto para adicionar, renomear, alterar cores ou ordenar grupos. Os grupos são partilhados no espaço e independentes do estado. Defina o grupo nos detalhes do cartão ou agrupe o quadro por Grupo e arraste o cartão para uma coluna. Antes de eliminar um grupo em uso, retire-o dos cartões.',
    ],
  },
  {
    id: 'labels',
    category: 'Workflow & organization',
    en: [
      'How do I manage and apply labels?',
      'Use Manage labels above the board or inside a card. Create labels, choose their colors, rename, and reorder them, then save. Select one or more labels in card Details and Save task. Labels appear in cards on the board, list, and editor. A label in use must be removed from its cards before deletion.',
    ],
    pt: [
      'Como giro e aplico etiquetas?',
      'Use Gerir etiquetas acima do quadro ou dentro de um cartão. Crie etiquetas, escolha cores, renomeie e ordene; depois guarde. Selecione uma ou mais etiquetas em Detalhes e escolha Guardar tarefa. As etiquetas aparecem nos cartões do quadro, da lista e do editor. Antes de eliminar uma etiqueta em uso, retire-a dos cartões.',
    ],
  },
  {
    id: 'swimlanes',
    category: 'Workflow & organization',
    en: [
      'How do I add, collapse, or move cards between swimlanes?',
      'Choose Manage swimlanes in the project. Add and name rows, then save. Rows start expanded; click a row heading to collapse or expand it. Drag cards between rows or choose a swimlane in card Details. Swimlanes belong to their project and expand again when you revisit or reload it.',
    ],
    pt: [
      'Como adiciono ou recolho faixas e movo cartões entre elas?',
      'Escolha Gerir faixas no projeto. Adicione e dê nome às linhas; depois guarde. As faixas começam expandidas; clique no cabeçalho para recolher ou expandir. Arraste cartões entre faixas ou escolha uma faixa em Detalhes. As faixas pertencem ao respetivo projeto e voltam a expandir quando o reabre ou recarrega.',
    ],
  },
  {
    id: 'filters',
    category: 'Workflow & organization',
    en: [
      'How do I find or filter cards?',
      'Use header search to match card titles, IDs, or labels. Open Filter to narrow by priority, assignee, bucket, swimlane, status, or card metrics such as open, completed, and overdue. Clear filters restores the view. My tasks starts with your assignments across active projects and resets project filters when opened.',
    ],
    pt: [
      'Como encontro ou filtro cartões?',
      'Use a pesquisa do cabeçalho para procurar títulos, identificadores ou etiquetas. Abra Filtrar para restringir por prioridade, responsável, grupo, faixa, estado ou métricas como em aberto, concluídos e em atraso. Limpar filtros repõe a vista. As minhas tarefas começa com as suas atribuições nos projetos ativos e repõe os filtros do projeto ao abrir.',
    ],
  },
  {
    id: 'dashboards',
    category: 'Dashboards & exports',
    en: [
      'Where can I see project and workspace metrics?',
      'Open the Dashboard tab inside a project for that project’s cards. Overview shows metrics across active projects in the current workspace. My tasks also offers a dashboard for your assignments. Click a KPI, a chart day, a status bar, or a bucket/swimlane row to inspect matching cards.',
    ],
    pt: [
      'Onde vejo as métricas dos projetos e do espaço?',
      'Abra o separador Painel de um projeto para ver os respetivos cartões. Visão geral apresenta métricas dos projetos ativos do espaço atual. As minhas tarefas também tem um painel das suas atribuições. Clique num indicador, num dia do gráfico, numa barra de estado ou numa linha de grupo/faixa para ver os cartões correspondentes.',
    ],
  },
  {
    id: 'metrics',
    category: 'Dashboards & exports',
    en: [
      'How are completion, overdue, and workload calculated?',
      'Completed cards are those in a status marked complete. Overdue means an open card with a due date before today; cards without dates never count as overdue. Workload counts an open card once for each assignee, so shared assignments may total more than the unique card count. Archived projects are excluded from workspace metrics.',
    ],
    pt: [
      'Como são calculados a conclusão, o atraso e a carga de trabalho?',
      'Os cartões concluídos estão num estado marcado como concluído. Em atraso significa um cartão aberto com data limite anterior a hoje; cartões sem data nunca contam como atrasados. A carga de trabalho conta um cartão aberto por cada responsável, pelo que o total pode superar o número de cartões únicos. Os projetos arquivados não entram nas métricas do espaço.',
    ],
  },
  {
    id: 'dashboard-filters',
    category: 'Dashboards & exports',
    en: [
      'How do I see bucket and swimlane performance?',
      'Use the Bucket and Swimlane selectors inside a dashboard. KPIs, charts, and performance rows follow these filters. Bucket and swimlane rows show total, open, completed, and late cards. Use Clear dashboard filters to restore the full dashboard scope. List grouping also shows per-group counts.',
    ],
    pt: [
      'Como vejo o desempenho de grupos e faixas?',
      'Use os seletores Grupo e Faixa dentro de um painel. Os indicadores, gráficos e linhas de desempenho acompanham estes filtros. As linhas mostram cartões totais, abertos, concluídos e atrasados. Use Limpar filtros do painel para repor o âmbito completo. O agrupamento da lista também mostra contagens por conjunto.',
    ],
  },
  {
    id: 'pdf',
    category: 'Dashboards & exports',
    en: [
      'How do I export a dashboard as PDF?',
      'Open a project dashboard, My tasks dashboard, or Overview and choose Export PDF. The report includes the current scope and filters, KPIs, charts, workflow counts, workload, bucket/swimlane performance, checklists, and recent card activity. It downloads in the selected language with a date and page numbers.',
    ],
    pt: [
      'Como exporto um painel para PDF?',
      'Abra o painel de um projeto, de As minhas tarefas ou a Visão geral e escolha Exportar PDF. O relatório inclui o âmbito e filtros atuais, indicadores, gráficos, contagens por estado, carga de trabalho, desempenho de grupos/faixas, listas de verificação e atividade recente. É transferido no idioma selecionado, com data e números de página.',
    ],
  },
  {
    id: 'json',
    category: 'Dashboards & exports',
    en: [
      'How do I export project data?',
      'Open a project, select the three-dot Project options menu, and choose Export project. A JSON file downloads with the project and its cards. Attachments are represented by metadata; their file contents are not included in the JSON export.',
    ],
    pt: [
      'Como exporto os dados de um projeto?',
      'Abra um projeto, selecione o menu de três pontos Opções do projeto e escolha Exportar projeto. É transferido um ficheiro JSON com o projeto e os cartões. Os anexos aparecem como metadados; o conteúdo dos ficheiros não é incluído na exportação JSON.',
    ],
  },
  {
    id: 'card-url',
    category: 'Links & sharing',
    en: [
      'How do I get a direct URL for a card?',
      'Open a saved card and choose Copy link at the top of the side panel. The browser address also updates to the card URL. Opening it selects the workspace and project and opens that card. Direct links do not grant access to another account. Existing links with lowercase card IDs still work.',
    ],
    pt: [
      'Como obtenho uma ligação direta para um cartão?',
      'Abra um cartão guardado e escolha Copiar ligação no topo do painel lateral. O endereço do navegador também passa a ser a ligação do cartão. Ao abri-la, são selecionados o espaço e o projeto e abre-se esse cartão. Uma ligação direta não dá acesso a outra conta. As ligações antigas com identificadores em minúsculas continuam a funcionar.',
    ],
  },
  {
    id: 'share',
    category: 'Links & sharing',
    en: [
      'How do I share a card for someone to view?',
      'Open a saved card and choose Share link. Select Anyone signed in with the link or Workspace members only, choose an expiration, and Create share link. Copy the generated link. It opens a read-only card with details, files, and history after sign-in; it does not allow editing.',
    ],
    pt: [
      'Como partilho um cartão para outra pessoa o consultar?',
      'Abra um cartão guardado e escolha Ligação de partilha. Selecione Qualquer pessoa com sessão iniciada e a ligação ou Apenas membros do espaço de trabalho, escolha a validade e prima Criar ligação de partilha. Copie a ligação gerada. Após iniciar sessão, abre um cartão apenas de leitura com detalhes, ficheiros e histórico; não permite editar.',
    ],
  },
  {
    id: 'revoke',
    category: 'Links & sharing',
    en: [
      'How do I change access, set expiration, or revoke a share link?',
      'Reopen Share link on the card. Change the audience or expiration and use Replace link with these settings; copy and send the new link. The previous link stops working. Revoke link disables it without creating a replacement. Expired links show that the card is unavailable.',
    ],
    pt: [
      'Como altero o acesso, a validade ou revogo uma ligação?',
      'Volte a abrir Ligação de partilha no cartão. Altere o público ou a validade e use Substituir ligação com estas definições; copie e envie a nova ligação. A anterior deixa de funcionar. Revogar ligação desativa-a sem criar outra. As ligações expiradas mostram que o cartão está indisponível.',
    ],
  },
  {
    id: 'share-unavailable',
    category: 'Links & sharing',
    en: [
      'Why can’t someone open my shared card?',
      'Check that the link has not expired or been revoked, the card still exists, and the person is signed in with an allowed account. The link must point to a server the recipient can reach. A localhost address only works on the computer running Kanbada; use your deployed application address for other devices.',
    ],
    pt: [
      'Porque não consegue alguém abrir o meu cartão partilhado?',
      'Verifique se a ligação não expirou nem foi revogada, se o cartão existe e se a pessoa iniciou sessão com uma conta permitida. A ligação deve apontar para um servidor acessível ao destinatário. Um endereço localhost só funciona no computador que executa o Kanbada; use o endereço da aplicação publicada para outros dispositivos.',
    ],
  },
];
