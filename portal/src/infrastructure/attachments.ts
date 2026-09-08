import type { Attachment } from '../domain/models';
import { accountStoragePrefix } from './accountStorage';
import { apiEnabled, apiRequest, downloadApi } from './apiClient';
import { activeWorkspaceId } from './remoteRepository';
export type { Attachment } from '../domain/models';
function database(prefix = accountStoragePrefix()): Promise<IDBDatabase> {
  return new Promise((resolve, reject) => {
    const request = indexedDB.open(prefix + 'kanbada-files', 1);
    request.onupgradeneeded = () => request.result.createObjectStore('files');
    request.onsuccess = () => resolve(request.result);
    request.onerror = () => reject(request.error);
  });
}
async function transaction<T>(
  mode: IDBTransactionMode,
  action: (store: IDBObjectStore) => IDBRequest<T>,
  prefix = accountStoragePrefix(),
): Promise<T> {
  const db = await database(prefix);
  try {
    return await new Promise((resolve, reject) => {
      const tx = db.transaction('files', mode);
      const request = action(tx.objectStore('files'));
      tx.oncomplete = () => resolve(request.result);
      tx.onerror = () => reject(tx.error);
      tx.onabort = () => reject(tx.error);
    });
  } finally {
    db.close();
  }
}
// Browser-only file storage used exclusively when VITE_DATA_MODE=local.
const localFiles = {
  async upload(file: File): Promise<Attachment> {
    const attachment = {
      id: crypto.randomUUID(),
      name: file.name,
      size: file.size,
      type: file.type,
      addedAt: new Date().toISOString(),
    };
    await transaction('readwrite', (store) => store.put(file, attachment.id));
    return attachment;
  },
  async download(attachment: Attachment, prefix = accountStoragePrefix()) {
    const file = await transaction<Blob | undefined>(
      'readonly',
      (store) => store.get(attachment.id),
      prefix,
    );
    if (!file) throw new Error('This file is unavailable in this browser.');
    const url = URL.createObjectURL(file);
    const a = document.createElement('a');
    a.href = url;
    a.download = attachment.name;
    a.click();
    setTimeout(() => URL.revokeObjectURL(url), 1000);
  },
  async remove(id: string) {
    await transaction('readwrite', (store) => store.delete(id));
  },
};

export const fileRepository = apiEnabled
  ? {
      async upload(file: File): Promise<Attachment> {
        const form = new FormData();
        form.append('file', file);
        return apiRequest('/workspaces/' + encodeURIComponent(activeWorkspaceId()) + '/files', {
          method: 'POST',
          body: form,
        });
      },
      async download(attachment: Attachment, prefix = '') {
        return downloadApi(
          prefix.startsWith('share:')
            ? '/shares/' + encodeURIComponent(prefix.slice(6)) + '/files/' + attachment.id
            : '/files/' + attachment.id,
          attachment.name,
        );
      },
      async remove(id: string) {
        await apiRequest('/files/' + id, { method: 'DELETE' });
      },
    }
  : localFiles;
