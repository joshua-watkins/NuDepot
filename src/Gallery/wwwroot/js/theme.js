// Follows the system's light or dark setting with Bootstrap's color modes. It runs in the page's head, before the
// page is drawn, so it doesn't flash light first.
(() => {
    'use strict';

    const dark = window.matchMedia('(prefers-color-scheme: dark)');
    const apply = () => document.documentElement.setAttribute('data-bs-theme', dark.matches ? 'dark' : 'light');

    apply();
    dark.addEventListener('change', apply);
})();
