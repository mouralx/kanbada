import type { Project } from './models';
export const isActivitiesProject = (project: Project) =>
  project.id === 'my-activities' || project.system === 'activities';
export const makeActivitiesProject = (): Project => ({
  id: 'my-activities',
  name: 'My activities',
  color: '#aac3e2',
  description: 'Your everyday work, ideas, and personal to-dos.',
  archived: false,
  system: 'activities',
});
export function ensureActivitiesProject(projects: Project[]): Project[] {
  const existing =
    projects.find(isActivitiesProject) ??
    projects.find((project) => project.name.trim().toLowerCase() === 'my activities');
  const personal = existing
    ? { ...existing, name: 'My activities', archived: false, system: 'activities' as const }
    : makeActivitiesProject();
  return [
    personal,
    ...projects.filter((project) => project !== existing && !isActivitiesProject(project)),
  ];
}
