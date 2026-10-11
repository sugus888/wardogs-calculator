'use strict';

/* =========================
   OVERLAY SETTINGS
   ========================= */

const fs = require('node:fs');
const path = require('node:path');

const OFFICIAL_URL = 'https://wardogs-artillery.com/';

const OPACITY_MIN = 0.2;
const OPACITY_MAX = 1;
const OPACITY_STEP = 0.1;

const MODES = Object.freeze(['desktop', 'mobile']);

const DEFAULT_SETTINGS = Object.freeze({
    bounds: null,
    opacity: 0.85,
    // The map-first mobile layout fits a small overlay window best.
    mode: 'mobile'
});

function clampOpacity(value) {
    const number = Number(value);

    if (!Number.isFinite(number)) {
        return DEFAULT_SETTINGS.opacity;
    }

    const clamped = Math.min(
        OPACITY_MAX,
        Math.max(OPACITY_MIN, number)
    );

    // Snap to 5% steps so repeated hotkey steps do not drift.
    return Math.round(clamped * 20) / 20;
}

function sanitizeBounds(bounds) {
    if (!bounds || typeof bounds !== 'object') {
        return null;
    }

    const keys = ['x', 'y', 'width', 'height'];

    if (!keys.every(key => Number.isInteger(bounds[key]))) {
        return null;
    }

    if (bounds.width < 320 || bounds.height < 240) {
        return null;
    }

    return {
        x: bounds.x,
        y: bounds.y,
        width: bounds.width,
        height: bounds.height
    };
}

function sanitizeSettings(raw) {
    const source = raw && typeof raw === 'object' ? raw : {};

    return {
        bounds: sanitizeBounds(source.bounds),
        opacity: clampOpacity(source.opacity ?? DEFAULT_SETTINGS.opacity),
        mode: MODES.includes(source.mode) ? source.mode : DEFAULT_SETTINGS.mode
    };
}

/*
 * Only the official site is loaded by default. A local development
 * server (the repository's own `npm run dev` / `python -m http.server`)
 * can be selected with --url, but no other remote origin is accepted.
 */
function resolveBaseURL(argv) {
    const flag = argv.find(arg => arg.startsWith('--url='));

    if (!flag) {
        return OFFICIAL_URL;
    }

    let url;

    try {
        url = new URL(flag.slice('--url='.length));
    } catch (_) {
        throw new Error(`Invalid --url value: ${flag}`);
    }

    const localHost = ['localhost', '127.0.0.1', '[::1]'].includes(url.hostname);

    if (url.href.startsWith(OFFICIAL_URL)) {
        return OFFICIAL_URL;
    }

    if (url.protocol !== 'http:' || !localHost) {
        throw new Error(
            '--url only accepts the official site or a local http://localhost server'
        );
    }

    if (!url.pathname.endsWith('/')) {
        url.pathname += '/';
    }

    url.search = '';
    url.hash = '';

    return url.href;
}

function modeURL(baseURL, mode) {
    return mode === 'mobile'
        ? new URL('mobile/', baseURL).href
        : baseURL;
}

function isAllowedNavigation(targetURL, baseURL) {
    try {
        return new URL(targetURL).origin === new URL(baseURL).origin;
    } catch (_) {
        return false;
    }
}

function createSettingsStore(directory) {
    const file = path.join(directory, 'overlay-settings.json');

    let settings = sanitizeSettings(null);

    try {
        settings = sanitizeSettings(
            JSON.parse(fs.readFileSync(file, 'utf8'))
        );
    } catch (_) {
        // Missing or corrupt settings fall back to defaults.
    }

    function save() {
        try {
            fs.mkdirSync(directory, { recursive: true });
            fs.writeFileSync(file, JSON.stringify(settings, null, 2));
        } catch (error) {
            console.error('Could not save overlay settings:', error);
        }
    }

    return {
        get: () => ({ ...settings }),
        update(patch) {
            settings = sanitizeSettings({ ...settings, ...patch });
            save();
            return { ...settings };
        }
    };
}

module.exports = {
    OFFICIAL_URL,
    OPACITY_MIN,
    OPACITY_MAX,
    OPACITY_STEP,
    MODES,
    clampOpacity,
    sanitizeSettings,
    resolveBaseURL,
    modeURL,
    isAllowedNavigation,
    createSettingsStore
};
