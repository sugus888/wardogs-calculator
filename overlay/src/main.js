'use strict';

/* =========================
   WARDOGS MAP OVERLAY
   =========================
 *
 * A frameless, always-on-top window that shows the official
 * WARDOGS Artillery Calculator. The site is loaded unmodified in
 * its own sandboxed view; the overlay only adds window behaviour
 * (opacity, click-through, hotkeys, tray). It does not read or
 * touch the game process in any way.
 */

const path = require('node:path');

const {
    app,
    BaseWindow,
    WebContentsView,
    Menu,
    Tray,
    globalShortcut,
    ipcMain,
    nativeImage,
    screen,
    session,
    shell
} = require('electron');

const {
    OPACITY_STEP,
    MODES,
    clampOpacity,
    resolveBaseURL,
    modeURL,
    isAllowedNavigation,
    createSettingsStore
} = require('./settings');

const TOOLBAR_HEIGHT = 30;
const SITE_PARTITION = 'persist:wardogs-site';
const ICON_PATH = path.join(__dirname, '..', 'build', 'icon.png');

const HOTKEYS = Object.freeze({
    toggleVisible: 'CommandOrControl+Shift+O',
    toggleClickThrough: 'CommandOrControl+Shift+C',
    opacityUp: 'CommandOrControl+Shift+Up',
    opacityDown: 'CommandOrControl+Shift+Down'
});

const SITE_PERMISSIONS = new Set([
    'clipboard-read',
    'clipboard-sanitized-write',
    'fullscreen'
]);

let baseURL;
let store;
let win = null;
let toolbarView = null;
let siteView = null;
let tray = null;
let clickThrough = false;

/* =========================
   STATE
   ========================= */

function overlayState() {
    const settings = store.get();

    return {
        opacity: settings.opacity,
        mode: settings.mode,
        clickThrough,
        hotkeys: HOTKEYS
    };
}

function broadcastState() {
    if (toolbarView && !toolbarView.webContents.isDestroyed()) {
        toolbarView.webContents.send('overlay:state', overlayState());
    }

    refreshTrayMenu();
}

function setOpacity(value) {
    const { opacity } = store.update({ opacity: clampOpacity(value) });

    win?.setOpacity(opacity);
    broadcastState();
}

function setClickThrough(enabled) {
    clickThrough = Boolean(enabled);

    if (win) {
        win.setIgnoreMouseEvents(clickThrough);

        // While click-through is on the overlay must never steal focus
        // from the game.
        win.setFocusable(!clickThrough);

        if (clickThrough) {
            win.blur();
        }
    }

    broadcastState();
}

function setMode(mode) {
    if (!MODES.includes(mode)) {
        return;
    }

    store.update({ mode });
    siteView?.webContents.loadURL(modeURL(baseURL, mode));
    broadcastState();
}

function toggleVisible() {
    if (!win) {
        return;
    }

    if (win.isVisible()) {
        win.hide();
    } else {
        win.showInactive();
        win.setAlwaysOnTop(true, 'screen-saver');
    }

    refreshTrayMenu();
}

/* =========================
   WINDOW
   ========================= */

function initialBounds() {
    const saved = store.get().bounds;

    if (saved) {
        const visible = screen.getAllDisplays().some(display => {
            const area = display.workArea;

            return (
                saved.x < area.x + area.width &&
                saved.x + saved.width > area.x &&
                saved.y < area.y + area.height &&
                saved.y + saved.height > area.y
            );
        });

        if (visible) {
            return saved;
        }
    }

    const area = screen.getPrimaryDisplay().workArea;
    const width = Math.round(area.width * 0.32);
    const height = Math.round(area.height * 0.55);

    return {
        x: area.x + area.width - width - 24,
        y: area.y + 24,
        width,
        height
    };
}

function layoutViews() {
    if (!win) {
        return;
    }

    const { width, height } = win.getContentBounds();

    toolbarView.setBounds({ x: 0, y: 0, width, height: TOOLBAR_HEIGHT });
    siteView.setBounds({
        x: 0,
        y: TOOLBAR_HEIGHT,
        width,
        height: Math.max(0, height - TOOLBAR_HEIGHT)
    });
}

function hardenSiteView(contents) {
    // Keep the overlay on the calculator site; everything else
    // (Ko-fi, GitHub, Discord links, ...) opens in the normal browser.
    const openExternally = url => {
        if (/^https?:\/\//i.test(url)) {
            shell.openExternal(url);
        }
    };

    contents.setWindowOpenHandler(({ url }) => {
        if (isAllowedNavigation(url, baseURL)) {
            contents.loadURL(url);
        } else {
            openExternally(url);
        }

        return { action: 'deny' };
    });

    contents.on('will-navigate', (event, url) => {
        if (!isAllowedNavigation(url, baseURL)) {
            event.preventDefault();
            openExternally(url);
        }
    });

    contents.on('will-redirect', (event, url) => {
        if (!isAllowedNavigation(url, baseURL)) {
            event.preventDefault();
        }
    });

    contents.on('before-input-event', (event, input) => {
        if (input.type !== 'keyDown') {
            return;
        }

        if (input.key === 'F5') {
            contents.reload();
            event.preventDefault();
        }
    });
}

function configureSiteSession() {
    const siteSession = session.fromPartition(SITE_PARTITION);

    siteSession.setPermissionRequestHandler(
        (contents, permission, callback, details) => {
            callback(
                SITE_PERMISSIONS.has(permission) &&
                isAllowedNavigation(details.requestingUrl, baseURL)
            );
        }
    );

    siteSession.setPermissionCheckHandler(
        (contents, permission, requestingOrigin) =>
            SITE_PERMISSIONS.has(permission) &&
            isAllowedNavigation(requestingOrigin, baseURL)
    );
}

function createWindow() {
    const settings = store.get();

    win = new BaseWindow({
        ...initialBounds(),
        minWidth: 320,
        minHeight: 240,
        frame: false,
        alwaysOnTop: true,
        backgroundColor: '#14161a',
        title: 'WARDOGS Map Overlay',
        icon: ICON_PATH,
        show: false
    });

    // 'screen-saver' keeps the overlay above borderless-fullscreen games.
    win.setAlwaysOnTop(true, 'screen-saver');
    win.setOpacity(settings.opacity);

    toolbarView = new WebContentsView({
        webPreferences: {
            preload: path.join(__dirname, 'toolbar', 'preload.js'),
            contextIsolation: true,
            sandbox: true,
            nodeIntegration: false
        }
    });

    siteView = new WebContentsView({
        webPreferences: {
            partition: SITE_PARTITION,
            contextIsolation: true,
            sandbox: true,
            nodeIntegration: false
        }
    });

    win.contentView.addChildView(siteView);
    win.contentView.addChildView(toolbarView);

    hardenSiteView(siteView.webContents);

    toolbarView.webContents.loadFile(
        path.join(__dirname, 'toolbar', 'index.html')
    );
    siteView.webContents.loadURL(modeURL(baseURL, settings.mode));

    layoutViews();

    win.on('resize', layoutViews);

    const saveBounds = () => {
        if (win && !win.isMinimized()) {
            store.update({ bounds: win.getBounds() });
        }
    };

    win.on('moved', saveBounds);
    win.on('resized', saveBounds);
    win.on('close', saveBounds);

    win.on('closed', () => {
        win = null;
        toolbarView = null;
        siteView = null;
        app.quit();
    });

    toolbarView.webContents.once('did-finish-load', () => {
        win.show();
        broadcastState();
    });
}

/* =========================
   TOOLBAR IPC
   ========================= */

function fromToolbar(event) {
    return Boolean(toolbarView) && event.sender === toolbarView.webContents;
}

function registerIPC() {
    ipcMain.handle('overlay:get-state', event =>
        fromToolbar(event) ? overlayState() : null
    );

    const actions = {
        'set-opacity': value => setOpacity(value),
        'toggle-click-through': () => setClickThrough(!clickThrough),
        'set-mode': mode => setMode(mode),
        reload: () => siteView?.webContents.reload(),
        back: () => {
            const history = siteView?.webContents.navigationHistory;

            if (history?.canGoBack()) {
                history.goBack();
            }
        },
        hide: () => toggleVisible(),
        quit: () => app.quit()
    };

    ipcMain.on('overlay:action', (event, name, value) => {
        if (fromToolbar(event) && Object.hasOwn(actions, name)) {
            actions[name](value);
        }
    });
}

/* =========================
   HOTKEYS & TRAY
   ========================= */

function registerHotkeys() {
    const bindings = [
        [HOTKEYS.toggleVisible, toggleVisible],
        [HOTKEYS.toggleClickThrough, () => setClickThrough(!clickThrough)],
        [HOTKEYS.opacityUp, () => setOpacity(store.get().opacity + OPACITY_STEP)],
        [HOTKEYS.opacityDown, () => setOpacity(store.get().opacity - OPACITY_STEP)]
    ];

    for (const [accelerator, handler] of bindings) {
        if (!globalShortcut.register(accelerator, handler)) {
            console.warn(`Hotkey already in use by another program: ${accelerator}`);
        }
    }
}

function refreshTrayMenu() {
    if (!tray) {
        return;
    }

    const { opacity, mode } = store.get();

    const opacityItems = [1, 0.85, 0.7, 0.5, 0.3].map(value => ({
        label: `${Math.round(value * 100)}%`,
        type: 'radio',
        checked: opacity === value,
        click: () => setOpacity(value)
    }));

    tray.setContextMenu(Menu.buildFromTemplate([
        {
            label: win?.isVisible() ? 'Hide overlay' : 'Show overlay',
            accelerator: HOTKEYS.toggleVisible,
            click: toggleVisible
        },
        {
            label: 'Click-through (mouse passes to game)',
            type: 'checkbox',
            checked: clickThrough,
            accelerator: HOTKEYS.toggleClickThrough,
            click: item => setClickThrough(item.checked)
        },
        { label: 'Opacity', submenu: opacityItems },
        {
            label: 'Layout',
            submenu: [
                {
                    label: 'Desktop UI',
                    type: 'radio',
                    checked: mode === 'desktop',
                    click: () => setMode('desktop')
                },
                {
                    label: 'Compact (mobile UI)',
                    type: 'radio',
                    checked: mode === 'mobile',
                    click: () => setMode('mobile')
                }
            ]
        },
        {
            label: 'Reset position',
            click: () => {
                store.update({ bounds: null });
                win?.setBounds(initialBounds());
            }
        },
        { type: 'separator' },
        { label: 'Quit', click: () => app.quit() }
    ]));
}

function createTray() {
    tray = new Tray(nativeImage.createFromPath(ICON_PATH));
    tray.setToolTip('WARDOGS Map Overlay');
    tray.on('click', toggleVisible);
    refreshTrayMenu();
}

/* =========================
   STARTUP
   ========================= */

if (!app.requestSingleInstanceLock()) {
    app.quit();
} else {
    try {
        baseURL = resolveBaseURL(process.argv);
    } catch (error) {
        console.error(error.message);
        app.exit(1);
    }

    app.on('second-instance', () => {
        if (win && !win.isVisible()) {
            toggleVisible();
        }
    });

    app.whenReady().then(() => {
        store = createSettingsStore(app.getPath('userData'));

        configureSiteSession();
        registerIPC();
        createWindow();
        createTray();
        registerHotkeys();
    });

    app.on('will-quit', () => {
        globalShortcut.unregisterAll();
    });

    app.on('window-all-closed', () => {
        app.quit();
    });
}
