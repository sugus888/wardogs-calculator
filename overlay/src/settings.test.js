'use strict';

const assert = require('node:assert/strict');
const fs = require('node:fs');
const os = require('node:os');
const path = require('node:path');
const test = require('node:test');

const {
    OFFICIAL_URL,
    clampOpacity,
    sanitizeSettings,
    resolveBaseURL,
    modeURL,
    isAllowedNavigation,
    createSettingsStore
} = require('./settings');

test('clampOpacity keeps the window visible and snaps to 5% steps', () => {
    assert.equal(clampOpacity(0), 0.2);
    assert.equal(clampOpacity(5), 1);
    assert.equal(clampOpacity(0.85), 0.85);
    assert.equal(clampOpacity(0.85 + 0.1), 0.95);
    assert.equal(clampOpacity(0.2 - 0.1), 0.2);
    assert.equal(clampOpacity('nope'), 0.85);
});

test('sanitizeSettings rejects malformed values', () => {
    assert.deepEqual(
        sanitizeSettings({
            bounds: { x: 1, y: 2, width: 100, height: 100 },
            opacity: 'x',
            mode: 'evil'
        }),
        { bounds: null, opacity: 0.85, mode: 'mobile' }
    );

    assert.deepEqual(
        sanitizeSettings({
            bounds: { x: -10, y: 20, width: 800, height: 600, extra: 1 },
            opacity: 0.5,
            mode: 'mobile'
        }),
        {
            bounds: { x: -10, y: 20, width: 800, height: 600 },
            opacity: 0.5,
            mode: 'mobile'
        }
    );
});

test('resolveBaseURL defaults to the official site', () => {
    assert.equal(resolveBaseURL([]), OFFICIAL_URL);
    assert.equal(resolveBaseURL(['--url=https://wardogs-artillery.com/ru/']), OFFICIAL_URL);
});

test('resolveBaseURL accepts only a local development server', () => {
    assert.equal(
        resolveBaseURL(['--url=http://localhost:8000']),
        'http://localhost:8000/'
    );
    assert.equal(
        resolveBaseURL(['--url=http://127.0.0.1:8000/sub?x=1#y']),
        'http://127.0.0.1:8000/sub/'
    );

    for (const url of [
        'https://example.com/',
        'http://example.com/',
        'https://localhost:8000/',
        'https://wardogs-artillery.com.evil.test/',
        'file:///C:/index.html',
        'not a url'
    ]) {
        assert.throws(() => resolveBaseURL([`--url=${url}`]), url);
    }
});

test('modeURL switches between desktop and mobile routes', () => {
    assert.equal(modeURL(OFFICIAL_URL, 'desktop'), OFFICIAL_URL);
    assert.equal(modeURL(OFFICIAL_URL, 'mobile'), `${OFFICIAL_URL}mobile/`);
});

test('isAllowedNavigation stays on the base origin', () => {
    assert.equal(isAllowedNavigation(`${OFFICIAL_URL}mobile/?lobby=AB12`, OFFICIAL_URL), true);
    assert.equal(isAllowedNavigation('https://ko-fi.com/x', OFFICIAL_URL), false);
    assert.equal(isAllowedNavigation('https://wardogs-artillery.com.evil.test/', OFFICIAL_URL), false);
    assert.equal(isAllowedNavigation('http://wardogs-artillery.com/', OFFICIAL_URL), false);
    assert.equal(isAllowedNavigation('garbage', OFFICIAL_URL), false);
});

test('settings store persists and recovers from corrupt files', () => {
    const directory = fs.mkdtempSync(path.join(os.tmpdir(), 'overlay-settings-'));

    try {
        const store = createSettingsStore(directory);

        assert.equal(store.get().opacity, 0.85);
        store.update({ opacity: 0.4, mode: 'desktop' });

        assert.deepEqual(createSettingsStore(directory).get(), {
            bounds: null,
            opacity: 0.4,
            mode: 'desktop'
        });

        fs.writeFileSync(path.join(directory, 'overlay-settings.json'), '{oops');
        assert.equal(createSettingsStore(directory).get().opacity, 0.85);
    } finally {
        fs.rmSync(directory, { recursive: true, force: true });
    }
});
