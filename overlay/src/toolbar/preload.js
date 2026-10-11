'use strict';

const { contextBridge, ipcRenderer } = require('electron');

contextBridge.exposeInMainWorld('overlay', {
    getState: () => ipcRenderer.invoke('overlay:get-state'),
    onState: callback => {
        ipcRenderer.on('overlay:state', (_event, state) => callback(state));
    },
    action: (name, value) => ipcRenderer.send('overlay:action', name, value)
});
