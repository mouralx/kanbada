// Stable public entry point retained for integrations and existing browser checks.
// New code should import domain models and the repository from their own modules.
export { recordChanges } from './domain/cardHistory';
export type * from './domain/models';
export * from './domain/projectRules';
export {
  defaultBuckets,
  defaultLabels,
  defaultStatuses,
  initialState,
  migrate,
  statuses,
  workspaceKey,
} from './infrastructure/local/localRepository';
export { repository } from './infrastructure/workspaceRepository';
