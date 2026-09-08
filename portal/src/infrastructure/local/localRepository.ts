import type { Definition, Member, State, Status, Task, Workspace } from '../../domain/models';
import {
  ensureActivitiesProject,
  isActivitiesProject,
  makeActivitiesProject,
} from '../../domain/projectRules';
import { accountStorage, currentStorageAccount } from '../accountStorage';
import { fileRepository } from '../attachments';
export const statuses: Status[] = ['Backlog', 'In progress', 'In review', 'Done'];
const tasks = [
  ['Map the customer journey', 'Backlog', 'High', 'Research', 'Sophie Chen', '11', 'journey'],
  ['Explore the new color palette', 'Backlog', 'Medium', 'Design', 'Alex Morgan', '12', ''],
  ['Audit existing components', 'Backlog', 'Low', 'Design', 'Jamie Wilson', '14', ''],
  ['Design the landing page', 'In progress', 'High', 'Design', 'Alex Morgan', '09', 'landing'],
  ['Build the navigation system', 'In progress', 'High', 'Development', 'Marcus Reed', '10', ''],
  ['Write our brand story', 'In progress', 'Medium', 'Content', 'Sophie Chen', '11', ''],
  ['Mobile onboarding flow', 'In review', 'Medium', 'Design', 'Jamie Wilson', '08', 'mobile'],
  ['Review accessibility guidelines', 'In review', 'High', 'Research', 'Marcus Reed', '09', ''],
  ['Define the project direction', 'Done', 'High', 'Strategy', 'Alex Morgan', '04', ''],
  ['Set up the design workspace', 'Done', 'Low', 'Design', 'Jamie Wilson', '05', ''],
  ['Competitor landscape analysis', 'Done', 'Medium', 'Research', 'Sophie Chen', '06', ''],
] as const;
export const defaultStatuses: Definition[] = statuses.map((name, i) => ({
  id: 'status-' + i,
  name,
  color: ['#9fa6ae', '#bd9949', '#a495c7', '#657b98'][i],
  complete: name === 'Done',
}));
export const defaultBuckets: Definition[] = [
  { id: 'bucket-1', name: 'Discovery', color: '#849bb8', complete: false },
  { id: 'bucket-2', name: 'Experience', color: '#b29bc9', complete: false },
];
export const defaultLabels: Definition[] = [
  'Design',
  'Development',
  'Research',
  'Content',
  'Strategy',
].map((name, i) => ({
  id: 'label-' + i,
  name,
  color: ['#9678b5', '#708bbd', '#718eb0', '#b29559', '#76958d'][i],
  complete: false,
}));
export const initialState: State = {
  notifications: [
    { id: 'welcome', message: 'Welcome to My Workspace', at: '2026-09-01T09:00:00.000Z' },
  ],
  workspace: { id: 'studio', name: 'My Workspace' },
  statuses: defaultStatuses,
  buckets: defaultBuckets,
  labels: defaultLabels,
  swimlanes: [],
  projects: [
    makeActivitiesProject(),
    {
      id: 'website',
      name: 'Website redesign',
      color: '#a6bddb',
      description: 'A fresh perspective. A better experience. Let’s build what’s next.',
    },
    {
      id: 'mobile',
      name: 'Mobile app',
      color: '#cbbdec',
      description: 'Small screen. Big possibilities. Building a more connected experience.',
    },
    {
      id: 'brand',
      name: 'Brand identity',
      color: '#eac19c',
      description: 'Finding our voice, shaping our story, and making our mark.',
    },
  ],
  members: [
    { name: 'Alex Morgan', initials: 'AM', color: '#e8c5a7', email: 'alex@studio.co' },
    { name: 'Sophie Chen', initials: 'SC', color: '#bdc6d2', email: 'sophie@studio.co' },
    { name: 'Marcus Reed', initials: 'MR', color: '#c8c3df', email: 'marcus@studio.co' },
    { name: 'Jamie Wilson', initials: 'JW', color: '#e5c6c8', email: 'jamie@studio.co' },
  ],
  tasks: tasks.map((t, i) => ({
    id: `KB-${101 + i}`,
    project: 'website',
    title: t[0],
    status: t[1],
    priority: t[2],
    labels: [t[3]],
    assignees: [t[4]],
    due: `2026-09-${t[5]}`,
    cover: t[6],
    bucket: i < 3 ? 'Discovery' : 'Experience',
    history: [
      {
        id: 'seed-' + i,
        at: '2026-09-01T09:00:00.000Z',
        actor: 'Alex Morgan',
        changes: ['Created card in ' + t[1]],
      },
    ],
    description:
      'Create a thoughtful, cohesive experience that puts our customers first. Explore ideas with the team and document the final direction.',
    comments:
      i % 3 === 0 ? ['Love the direction. Let’s explore this in our next design sync.'] : [],
    checklist: [
      { text: 'Explore references and gather inspiration', done: i > 3 },
      { text: 'Create a first iteration', done: i > 6 },
      { text: 'Share with the team for feedback', done: i > 7 },
    ],
  })),
  activity: [
    'Alex created the Website redesign project',
    'Sophie added customer research notes',
    'Jamie completed the design workspace',
  ],
};
// Replace this adapter with HTTP requests when your API is ready.
// The UI consumes the same asynchronous load/save contract in either mode.
export const workspaceKey = (id: string) =>
  id === 'studio' ? 'kanbada-v1' : 'kanbada-workspace-' + id;
const activeWorkspace = () => accountStorage.getItem('kanbada-active-workspace') || 'studio';
export const localRepository = {
  async load(): Promise<State> {
    const key = workspaceKey(activeWorkspace());
    const raw = accountStorage.getItem(key);
    if (raw) {
      try {
        const data = JSON.parse(raw);
        if (
          Array.isArray(data.tasks) &&
          Array.isArray(data.projects) &&
          Array.isArray(data.members) &&
          Array.isArray(data.activity)
        ) {
          const upgraded = migrate(data);
          try {
            accountStorage.setItem(key, JSON.stringify(upgraded));
          } catch {
            /* Keep loaded data if storage is full. */
          }
          return upgraded;
        }
      } catch {
        /* Restore initial data if storage is invalid. */
      }
    }
    const account = currentStorageAccount();
    if (account && account.id !== 'owner') {
      return {
        ...structuredClone(initialState),
        workspace: { id: 'studio', name: 'My Workspace' },
        projects: [makeActivitiesProject()],
        tasks: [],
        notifications: [],
        activity: [],
        buckets: [],
        labels: [],
        members: [
          {
            name: account.name,
            email: account.email,
            initials: account.name
              .split(' ')
              .map((n) => n[0])
              .slice(0, 2)
              .join(''),
            color: '#bec9d8',
          },
        ],
      };
    }
    return structuredClone(initialState);
  },
  async save(state: State): Promise<State> {
    const personal = state.projects.find(isActivitiesProject);
    if (!personal || personal.archived || personal.name !== 'My activities')
      throw new Error('My activities cannot be archived, deleted, or renamed.');
    accountStorage.setItem(workspaceKey(state.workspace.id), JSON.stringify(state));
    return structuredClone(state);
  },
  async listWorkspaces(): Promise<Workspace[]> {
    const hydrate = (workspace: Workspace): Workspace => {
      try {
        const stored = JSON.parse(accountStorage.getItem(workspaceKey(workspace.id)) || 'null');
        return { ...workspace, icon: stored?.workspace?.icon };
      } catch {
        return workspace;
      }
    };
    const personal = hydrate({ id: 'studio', name: 'My Workspace' });
    const raw = accountStorage.getItem('kanbada-workspaces-v1');
    if (raw) {
      try {
        const list = JSON.parse(raw);
        if (Array.isArray(list)) {
          const seen = new Set(['studio']);
          return [
            personal,
            ...list
              .filter((w: Workspace) => {
                if (!w || typeof w.id !== 'string' || typeof w.name !== 'string' || seen.has(w.id))
                  return false;
                seen.add(w.id);
                return true;
              })
              .map(hydrate),
          ];
        }
      } catch {
        /* Always retain the personal workspace. */
      }
    }
    return [personal];
  },
  async deleteWorkspace(id: string): Promise<State> {
    if (id === 'studio') throw new Error('My Workspace cannot be deleted.');
    const entries = await localRepository.listWorkspaces();
    if (!entries.some((w) => w.id === id))
      throw new Error('This workspace is no longer available.');
    const raw = accountStorage.getItem(workspaceKey(id));
    const removed: State | undefined = raw ? JSON.parse(raw) : undefined;
    const remaining = entries.filter((w) => w.id !== id);
    const retainedFiles = new Set<string>();
    for (const workspace of remaining) {
      const saved = accountStorage.getItem(workspaceKey(workspace.id));
      if (saved) {
        const state: State = JSON.parse(saved);
        for (const file of state.tasks.flatMap((task) => task.attachments ?? []))
          retainedFiles.add(file.id);
      }
    }
    const previousActive = accountStorage.getItem('kanbada-active-workspace');
    const previousCatalog = accountStorage.getItem('kanbada-workspaces-v1');
    try {
      if (activeWorkspace() === id) accountStorage.setItem('kanbada-active-workspace', 'studio');
      accountStorage.setItem(
        'kanbada-workspaces-v1',
        JSON.stringify(remaining.map(({ id, name }) => ({ id, name }))),
      );
      accountStorage.removeItem(workspaceKey(id));
    } catch (error) {
      if (previousActive === null) accountStorage.removeItem('kanbada-active-workspace');
      else accountStorage.setItem('kanbada-active-workspace', previousActive);
      if (previousCatalog === null) accountStorage.removeItem('kanbada-workspaces-v1');
      else accountStorage.setItem('kanbada-workspaces-v1', previousCatalog);
      throw error;
    }
    await Promise.allSettled(
      (removed?.tasks ?? [])
        .flatMap((task) => task.attachments ?? [])
        .filter((file) => !retainedFiles.has(file.id))
        .map((file) => fileRepository.remove(file.id)),
    );
    return localRepository.load();
  },
  async switchWorkspace(id: string): Promise<State> {
    const entries = await localRepository.listWorkspaces();
    if (!entries.some((w) => w.id === id))
      throw new Error('This workspace is no longer available.');
    const previous = activeWorkspace();
    accountStorage.setItem('kanbada-active-workspace', id);
    try {
      return await localRepository.load();
    } catch (e) {
      accountStorage.setItem('kanbada-active-workspace', previous);
      throw e;
    }
  },
  async createWorkspace(name: string, owner: Member): Promise<State> {
    const entries = await localRepository.listWorkspaces();
    name = name.trim();
    if (!name) throw new Error('Give your workspace a name.');
    if (entries.some((w) => w.name.toLowerCase() === name.toLowerCase()))
      throw new Error('A workspace with that name already exists.');
    const workspace = { id: crypto.randomUUID(), name };
    const state: State = {
      notifications: [],
      workspace,
      projects: [makeActivitiesProject()],
      tasks: [],
      members: [structuredClone(owner)],
      statuses: structuredClone(defaultStatuses),
      buckets: [],
      labels: [],
      swimlanes: [],
      activity: ['Created workspace “' + name + '”'],
    };
    await localRepository.save(state);
    accountStorage.setItem(
      'kanbada-workspaces-v1',
      JSON.stringify([...entries, workspace].map(({ id, name }) => ({ id, name }))),
    );
    accountStorage.setItem('kanbada-active-workspace', workspace.id);
    return state;
  },
};

// Upgrade existing browser data without replacing the user’s cards or projects.
export function migrate(data: State): State {
  const definitions = data.statuses?.length
    ? structuredClone(data.statuses)
    : structuredClone(defaultStatuses);
  for (const task of data.tasks)
    if (!definitions.some((s) => s.name === task.status))
      definitions.push({
        id: crypto.randomUUID(),
        name: task.status,
        color: '#78889d',
        complete: false,
      });
  const labelDefinitions = structuredClone(data.labels ?? defaultLabels);
  const migratedTasks = data.tasks.map((task) => {
    const { tag, ...current } = task as Task & { tag?: string };
    const names = current.labels ?? (tag ? [tag] : []);
    const labels = [
      ...new Set(
        names.map((name) => {
          const existing = labelDefinitions.find(
            (label) => label.name.toLowerCase() === name.toLowerCase(),
          );
          if (existing) return existing.name;
          labelDefinitions.push({
            id: crypto.randomUUID(),
            name,
            color: '#7490b0',
            complete: false,
          });
          return name;
        }),
      ),
    ];
    return { ...current, labels };
  });
  return {
    ...data,
    projects: ensureActivitiesProject(data.projects),
    notifications:
      data.notifications ??
      data.activity.map((message, index) => ({
        id: 'legacy-notification-' + index,
        message,
        at: new Date().toISOString(),
      })),
    labels: labelDefinitions,
    workspace:
      !data.workspace || data.workspace.id === 'studio'
        ? { ...data.workspace, id: 'studio', name: 'My Workspace' }
        : data.workspace,
    swimlanes: data.swimlanes ?? [],
    statuses: definitions,
    buckets: data.buckets ?? structuredClone(defaultBuckets),
    tasks: migratedTasks.map((task) => ({
      ...task,
      id: task.id.toUpperCase(),
      due: task.due ?? '',
      assignees:
        task.assignees ??
        ((task as Task & { assignee?: string }).assignee
          ? [(task as Task & { assignee: string }).assignee]
          : []),
      history: task.history ?? [
        {
          id: crypto.randomUUID(),
          at: new Date().toISOString(),
          actor: 'Workspace',
          changes: ['History tracking started for this existing card'],
        },
      ],
    })),
  };
}
