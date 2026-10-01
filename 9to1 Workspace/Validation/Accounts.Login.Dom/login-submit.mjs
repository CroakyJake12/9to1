// Future UI handler; injected transport has no activated backend or endpoint.
export function bindLoginSubmit(form, button, read, transport, report) {
  let pending, abort, disposed = false;
  const submit = event => {
    event.preventDefault();
    if (disposed || pending) return;
    const input = Object.freeze({ ...read(), nonce: crypto.randomUUID() });
    abort = new AbortController(); button.disabled = true;
    const signal = abort.signal;
    pending = Promise.resolve().then(() => transport.submit(input, signal))
      .then(result => { if (!disposed) report(result); })
      .catch(() => { if (!disposed) report({ kind: 'unknown' }); })
      .finally(() => { pending = undefined; abort = undefined; if (!disposed) button.disabled = false; });
  };
  form.addEventListener('submit', submit);
  return { cancel: () => abort?.abort(), dispose: () => {
    disposed = true; abort?.abort(); form.removeEventListener('submit', submit);
  }};
}
