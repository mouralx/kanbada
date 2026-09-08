import { useEffect, useState, type Dispatch, type SetStateAction } from 'react';
import type { State, Task } from '../../domain/models';
import { accountStoragePrefix } from '../../infrastructure/accountStorage';
import { repository } from '../../infrastructure/workspaceRepository';

type Options = {
  setProjectId: Dispatch<SetStateAction<string>>;
  setPage: Dispatch<SetStateAction<string>>;
  setDraft: Dispatch<SetStateAction<Task | null>>;
  setToast: Dispatch<SetStateAction<string>>;
};
/** Loads the active workspace and resolves direct card links and browser history. */
export function useWorkspaceLocation({ setProjectId, setPage, setDraft, setToast }: Options) {
  const [data, setData] = useState<State | null>(null);
  const [loadError, setLoadError] = useState('');
  useEffect(() => {
    let sequence = 0;
    let mounted = true;
    const openLocation = async () => {
      const request = ++sequence;
      try {
        const params = new URLSearchParams(window.location.search);
        const workspace = params.get('workspace');
        let loaded = await repository.load();
        if (workspace && workspace !== loaded.workspace.id)
          loaded = await repository.switchWorkspace(workspace);
        if (!mounted || request !== sequence) return;
        setData(loaded);
        const id = params.get('card');
        const task = loaded.tasks.find((task) => task.id.toUpperCase() === id?.toUpperCase());
        if (task) {
          setProjectId(task.project);
          setPage('Projects');
          setDraft(structuredClone(task));
        } else {
          setDraft(null);
          if (id) {
            setToast('This card is unavailable in this workspace.');
            const url = new URL(window.location.href);
            url.searchParams.delete('card');
            window.history.replaceState(null, '', url);
          }
          if (!loaded.projects.length) setPage('Overview');
        }
      } catch {
        if (mounted && request === sequence) {
          let loaded: State;
          try {
            loaded = await repository.load();
          } catch (error) {
            setLoadError(error instanceof Error ? error.message : 'Could not load workspace.');
            return;
          }
          setData(loaded);
          setDraft(null);
          setToast('This workspace or card is unavailable.');
          const url = new URL(window.location.href);
          url.searchParams.delete('card');
          url.searchParams.delete('workspace');
          window.history.replaceState(null, '', url);
        }
      }
    };
    void openLocation();
    window.addEventListener('popstate', openLocation);
    const refresh = (event: StorageEvent) => {
      if (accountStoragePrefix() && !event.key?.startsWith(accountStoragePrefix())) return;
      const key = event.key?.slice(accountStoragePrefix().length);
      if (
        key === 'kanbada-v1' ||
        key?.startsWith('kanbada-workspace-') ||
        key === 'kanbada-active-workspace'
      )
        repository
          .load()
          .then(setData)
          .catch((error) =>
            setLoadError(error instanceof Error ? error.message : 'Could not load workspace.'),
          );
    };
    window.addEventListener('storage', refresh);
    return () => {
      mounted = false;
      sequence++;
      window.removeEventListener('storage', refresh);
      window.removeEventListener('popstate', openLocation);
    };
  }, [setDraft, setPage, setProjectId, setToast]);

  return { data, setData, loadError };
}
