import type { State } from '../domain/models';

type Json = null | boolean | number | string | Json[] | { [key: string]: Json };
export type Change = { op: 'add' | 'remove' | 'replace'; path: string; value?: Json };
export type ChangeResult = { version: number; changes: Change[] };
const equal = (a: Json, b: Json) => JSON.stringify(a) === JSON.stringify(b);
const escape = (key: string) => key.replaceAll('~', '~0').replaceAll('/', '~1');
const identity = (value: Json): Json =>
  value && typeof value === 'object' && !Array.isArray(value)
    ? (value.id ?? value.email ?? value)
    : value;

/** Emits field changes and array insertions/removals, preserving untouched records and images. */
export function differences(before: Json, after: Json, path = ''): Change[] {
  if (equal(before, after)) return [];
  if (Array.isArray(before) && Array.isArray(after)) {
    const work = [...before];
    const changes: Change[] = [];
    for (let i = 0; i < after.length; i++) {
      const target = after[i];
      if (i < work.length && equal(identity(work[i]), identity(target))) {
        changes.push(...differences(work[i], target, `${path}/${i}`));
        work[i] = target;
      } else {
        const found = work.findIndex(
          (item, index) => index > i && equal(identity(item), identity(target)),
        );
        if (found >= 0) {
          for (let n = i; n < found; n++) changes.push({ op: 'remove', path: `${path}/${i}` });
          work.splice(i, found - i);
          changes.push(...differences(work[i], target, `${path}/${i}`));
          work[i] = target;
        } else {
          changes.push({ op: 'add', path: `${path}/${i}`, value: target });
          work.splice(i, 0, target);
        }
      }
    }
    for (let i = work.length - 1; i >= after.length; i--)
      changes.push({ op: 'remove', path: `${path}/${i}` });
    // Emptying a collection needs no list of every removed item.
    return after.length === 0 ? [{ op: 'replace', path, value: [] }] : changes;
  }
  if (
    before &&
    after &&
    typeof before === 'object' &&
    typeof after === 'object' &&
    !Array.isArray(before) &&
    !Array.isArray(after)
  ) {
    return [
      ...Object.keys(before)
        .filter((key) => !(key in after))
        .map((key): Change => ({ op: 'remove', path: `${path}/${escape(key)}` })),
      ...Object.keys(after).flatMap((key): Change[] =>
        key in before
          ? differences(before[key], after[key], `${path}/${escape(key)}`)
          : [{ op: 'add', path: `${path}/${escape(key)}`, value: after[key] }],
      ),
    ];
  }
  return [{ op: 'replace', path, value: after }];
}

/** History, versions and identity/permission fields are supplied exclusively by the server. */
function editable(state: State): Json {
  const result = JSON.parse(JSON.stringify(state)) as Record<string, Json>;
  delete result.version;
  for (const task of result.tasks as Record<string, Json>[]) delete task.history;
  for (const member of result.members as Record<string, Json>[]) {
    delete member.userId;
    delete member.invitationToken;
  }
  const workspace = result.workspace as Record<string, Json>;
  for (const field of ['id', 'ownerId', 'personal', 'canManage']) delete workspace[field];
  return result;
}
export const workspaceChanges = (before: State, after: State) =>
  differences(editable(before), editable(after));

/** Applies the canonical server delta to a copy of precisely the version used for the request. */
export function applyChanges(state: State, result: ChangeResult): State {
  const next = structuredClone(state);
  for (const change of result.changes) {
    const keys = change.path
      .slice(1)
      .split('/')
      .map((key) => key.replaceAll('~1', '/').replaceAll('~0', '~'));
    if (
      !change.path.startsWith('/') ||
      keys.some((key) => ['__proto__', 'constructor', 'prototype'].includes(key))
    )
      throw new Error('Invalid server change path.');
    let parent = next as unknown as Record<string, Json>;
    for (const key of keys.slice(0, -1)) parent = parent[key] as Record<string, Json>;
    const key = keys[keys.length - 1];
    if (Array.isArray(parent)) {
      const index = Number(key);
      if (change.op === 'remove') parent.splice(index, 1);
      else if (change.op === 'add') parent.splice(index, 0, structuredClone(change.value));
      else parent[index] = structuredClone(change.value);
    } else if (change.op === 'remove') delete parent[key];
    else parent[key] = structuredClone(change.value!);
  }
  next.version = result.version;
  return next;
}
