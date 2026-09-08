import { useRef, useState, type Dispatch, type SetStateAction } from 'react';
import type { State } from '../../domain/models';
import { repository } from '../../infrastructure/workspaceRepository';

/** Serializes local writes and publishes the canonical server version only after a successful save. */
export function useWorkspaceSave(
  data: State | null,
  setData: Dispatch<SetStateAction<State | null>>,
  setToast: Dispatch<SetStateAction<string>>,
) {
  const [saving, setSaving] = useState(false);
  const inFlight = useRef(false);
  const commit = async (next: State, message?: string, notify = true) => {
    if (!data || inFlight.current) return false;
    inFlight.current = true;
    try {
      if (notify && message)
        next = {
          ...next,
          notifications: [
            {
              id: crypto.randomUUID(),
              at: new Date().toISOString(),
              message: next.activity[0] !== data.activity[0] ? next.activity[0] : message,
            },
            ...next.notifications,
          ].slice(0, 100),
        };
      setSaving(true);
      const saved = await repository.save(next, data);
      setData(saved);
      if (message) setToast(message);
      return true;
    } catch (error) {
      setToast(error instanceof Error ? error.message : 'Could not save changes.');
      return false;
    } finally {
      inFlight.current = false;
      setSaving(false);
    }
  };

  return { saving, commit };
}
