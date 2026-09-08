import type { Task } from './models';
export function recordChanges(previous: Task | undefined, next: Task, actor: string): Task {
  const changes: string[] = [];
  if (!previous) {
    changes.push('Created card in ' + next.status);
    if (next.labels.length) changes.push('Labels: None → ' + next.labels.join(', '));
    for (const file of next.attachments ?? []) changes.push('Attached file: ' + file.name);
  } else {
    const fields = [
      'title',
      'description',
      'status',
      'priority',
      'due',
      'bucket',
      'swimlane',
    ] as const;
    const labels = {
      title: 'Title',
      description: 'Description',
      status: 'Status',
      priority: 'Priority',
      due: 'Due date',
      bucket: 'Bucket',
      swimlane: 'Swimlane',
    };
    for (const field of fields)
      if ((previous[field] ?? '') !== (next[field] ?? ''))
        changes.push(`${labels[field]}: ${previous[field] || 'None'} → ${next[field] || 'None'}`);
    if (JSON.stringify([...previous.labels].sort()) !== JSON.stringify([...next.labels].sort()))
      changes.push(
        `Labels: ${previous.labels.join(', ') || 'None'} → ${next.labels.join(', ') || 'None'}`,
      );
    if (
      JSON.stringify([...previous.assignees].sort()) !== JSON.stringify([...next.assignees].sort())
    )
      changes.push(
        `Assignees: ${previous.assignees.join(', ') || 'None'} → ${next.assignees.join(', ') || 'None'}`,
      );
    if (JSON.stringify(previous.checklist) !== JSON.stringify(next.checklist))
      changes.push(
        'Checklist updated: ' +
          next.checklist.filter((c) => c.done).length +
          '/' +
          next.checklist.length +
          ' complete',
      );
    for (const file of next.attachments ?? [])
      if (!previous.attachments?.some((a) => a.id === file.id))
        changes.push('Attached file: ' + file.name);
    for (const file of previous.attachments ?? [])
      if (!next.attachments?.some((a) => a.id === file.id))
        changes.push('Removed attachment: ' + file.name);
    for (const comment of next.comments.slice(previous.comments.length))
      changes.push('Added comment: ' + comment);
  }
  return {
    ...next,
    history: changes.length
      ? [
          ...(previous?.history ?? []),
          { id: crypto.randomUUID(), at: new Date().toISOString(), actor, changes },
        ]
      : (previous?.history ?? []),
  };
}
