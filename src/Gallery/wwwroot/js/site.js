// Shared by the slices' scripts, as window.nudepot. Written for browsers that support passkeys.
(() => {
    'use strict';

    // Copies text to the clipboard, then puts the focus back on elementToFocus, so the tab order isn't reset to
    // the start of the page.
    const copyTextToClipboard = async (text, elementToFocus) => {
        try {
            // The Clipboard API is only available on HTTPS pages and localhost.
            if (window.isSecureContext && navigator.clipboard) {
                await navigator.clipboard.writeText(text);
            } else {
                copyWithTextArea(text);
            }
        } catch (error) {
            console.error('Unable to copy to the clipboard', error);
        }

        elementToFocus?.focus();
    };

    // On pages served over HTTP, like a gallery on a local network, copy from a hidden text area instead.
    const copyWithTextArea = (text) => {
        const textArea = document.createElement('textarea');
        textArea.value = text;
        textArea.setAttribute('readonly', '');
        Object.assign(textArea.style, { position: 'fixed', top: '0', left: '0', opacity: '0' });

        document.body.appendChild(textArea);
        textArea.select();
        try {
            document.execCommand('copy');
        } finally {
            textArea.remove();
        }
    };

    // Calls one of the feed's endpoints, with the sign-in cookie. A failure throws its message.
    const request = async (method, url, body) => {
        const init = { method, credentials: 'same-origin', headers: {} };
        if (body instanceof FormData) {
            init.body = body;
        } else if (body !== undefined) {
            init.headers['Content-Type'] = 'application/json';
            init.body = JSON.stringify(body);
        }

        const response = await fetch(url, init);
        if (!response.ok) {
            const message = await response.text();
            const error = new Error(message || `Something went wrong (${response.status}).`);
            error.status = response.status;
            throw error;
        }

        const isJson = (response.headers.get('Content-Type') ?? '').includes('json');
        return isJson ? response.json() : null;
    };

    // Shows an error in the page's [data-manage-error] alert, or hides it.
    const showError = (error) => {
        const element = document.querySelector('[data-manage-error]');
        if (!element) return;

        element.textContent = error ? error.message || String(error) : '';
        element.hidden = !error;
    };

    // Disables the button while the promise runs, and shows its error.
    const busy = async (button, promise) => {
        button.disabled = true;
        showError(null);
        try {
            await promise;
        } catch (error) {
            showError(error);
        } finally {
            button.disabled = false;
        }
    };

    window.nudepot = { copyTextToClipboard, request, showError, busy };
})();
