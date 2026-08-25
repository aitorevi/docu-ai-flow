// Opens the <ConfirmModal /> present on the page and resolves true on Confirm, false on Cancel /
// backdrop / Escape. Falls back to window.confirm() if the modal markup is absent for any reason.
// Listeners are attached on open and removed on close so nothing leaks across calls.
export function confirmDialog(opts: { title: string; message: string; confirmLabel: string; variant?: 'danger' | 'primary' }): Promise<boolean> {
  const modal = document.getElementById('confirm-modal');
  const titleEl = document.getElementById('confirm-title');
  const messageEl = document.getElementById('confirm-message');
  const okBtn = modal?.querySelector<HTMLButtonElement>('.confirm-ok');
  const cancelBtn = modal?.querySelector<HTMLButtonElement>('.confirm-cancel');
  const backdrop = modal?.querySelector<HTMLElement>('[data-close]');
  if (!modal || !titleEl || !messageEl || !okBtn || !cancelBtn || !backdrop) {
    return Promise.resolve(window.confirm(opts.message));
  }

  titleEl.textContent = opts.title;
  messageEl.textContent = opts.message;
  okBtn.textContent = opts.confirmLabel;
  // Destructive by default (red). Non-destructive callers pass variant: 'primary' for the blue button.
  okBtn.classList.toggle('is-primary', opts.variant === 'primary');

  return new Promise<boolean>((resolve) => {
    const close = (result: boolean): void => {
      modal.hidden = true;
      okBtn.removeEventListener('click', onOk);
      cancelBtn.removeEventListener('click', onCancel);
      backdrop.removeEventListener('click', onCancel);
      document.removeEventListener('keydown', onKey);
      resolve(result);
    };
    const onOk = (): void => close(true);
    const onCancel = (): void => close(false);
    const onKey = (e: KeyboardEvent): void => { if (e.key === 'Escape') onCancel(); };

    okBtn.addEventListener('click', onOk);
    cancelBtn.addEventListener('click', onCancel);
    backdrop.addEventListener('click', onCancel);
    document.addEventListener('keydown', onKey);

    modal.hidden = false;
    cancelBtn.focus();
  });
}
