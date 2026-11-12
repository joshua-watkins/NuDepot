// Shared by the slices' scripts, as window.nudepot, and the behavior every page has. Pages have no inline scripts or
// event handlers, so their Content-Security-Policy only allows the website's own scripts. Written for browsers that
// support passkeys.
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

    // The settings a slice passes to its script, as JSON in a <script type="application/json"> element, which
    // doesn't run.
    const settings = (id) => {
        const element = document.getElementById(id);
        return element ? JSON.parse(element.textContent) : {};
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

    window.nudepot = { copyTextToClipboard, settings, request, showError, busy };

    // Copy buttons copy the commands in their box, without their prompts.
    for (const copy of document.querySelectorAll('[data-copy]')) {
        copy.addEventListener('click', () => {
            const lines = [...copy.closest('.command-box').querySelectorAll('.script-line')].map((line) => line.textContent);
            copyTextToClipboard(lines.join('\n'), copy);
        });
    }

    // Tabs, like how to install a package with each client. The page shows the first tab without scripts.
    for (const tabs of document.querySelectorAll('[data-tabs]')) {
        const buttons = [...tabs.querySelectorAll('[role="tab"]')];
        const panels = [...tabs.querySelectorAll('[role="tabpanel"]')];

        const select = (index, focus) => {
            buttons.forEach((button, i) => {
                const selected = i === index;
                button.setAttribute('aria-selected', String(selected));
                button.tabIndex = selected ? 0 : -1;
                panels[i].hidden = !selected;
            });
            if (focus) buttons[index].focus();
        };

        buttons.forEach((button, index) => {
            button.addEventListener('click', () => select(index, false));
            button.addEventListener('keydown', (event) => {
                const next = { ArrowRight: 1, ArrowLeft: -1 }[event.key];
                if (next === undefined) return;

                event.preventDefault();
                select((index + next + buttons.length) % buttons.length, true);
            });
        });
    }

    // A list that shows its first items, and the rest after its button is pressed.
    for (const button of document.querySelectorAll('[data-show-more]')) {
        const list = button.closest('[data-show-more-list]');
        const label = button.querySelector('span');

        button.addEventListener('click', () => {
            const showAll = list.classList.toggle('show-all');
            button.setAttribute('aria-expanded', String(showAll));
            button.querySelector('.icon').className = `icon ${showAll ? 'icon-dash-circle' : 'icon-plus-circle'}`;
            label.textContent = showAll ? 'Show less' : 'Show more';
        });
    }

    // Dropdowns are <details> elements, which close when something else is clicked, or Escape is pressed.
    const closeDropdowns = (except) => {
        for (const dropdown of document.querySelectorAll('details.dropdown[open]')) {
            if (dropdown !== except) dropdown.open = false;
        }
    };

    document.addEventListener('click', (event) => closeDropdowns(event.target.closest('details.dropdown')));
    document.addEventListener('keydown', (event) => {
        if (event.key !== 'Escape') return;

        const open = document.querySelector('details.dropdown[open]');
        if (!open) return;

        open.open = false;
        open.querySelector('summary').focus();
    });

    // Checkboxes that go to another page, like including prerelease packages in a search.
    for (const checkbox of document.querySelectorAll('input[data-navigate]')) {
        checkbox.addEventListener('change', () => window.location.assign(checkbox.dataset.navigate));
    }

    // Package icons that can't be loaded show the default icon. Some may have failed before this script ran.
    const useFallback = (image) => {
        if (image.dataset.usedFallback) return;

        image.dataset.usedFallback = 'true';
        image.src = image.dataset.fallback;
    };

    for (const image of document.querySelectorAll('img[data-fallback]')) {
        image.addEventListener('error', () => useFallback(image));
        if (image.complete && image.naturalWidth === 0) useFallback(image);
    }
})();
