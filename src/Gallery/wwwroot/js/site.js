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

    window.nudepot = { copyTextToClipboard };
})();
