// Renders a load-error state with a "Reintentar" button into `container`, wiring the button to
// `onRetry`. Used wherever an API fetch can fail transiently (backend restarting, timeout): the user
// retries in place instead of reloading the whole page and losing navigation/scroll state.
export function renderLoadError(container: HTMLElement, message: string, onRetry: () => void): void {
  container.innerHTML = `
    <div class="load-error">
      <span class="load-error-msg">${escapeHtml(message)}</span>
      <button type="button" class="load-error-retry">Reintentar</button>
    </div>`;
  container.querySelector<HTMLButtonElement>('.load-error-retry')
    ?.addEventListener('click', () => onRetry(), { once: true });
}

function escapeHtml(v: string): string {
  return v.replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;').replace(/"/g, '&quot;');
}
