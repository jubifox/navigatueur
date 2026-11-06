"use strict";
// The toolbar's and the tab column's only access to the app.
const { contextBridge, ipcRenderer } = require("electron");

contextBridge.exposeInMainWorld("nav", {
  send: (type, data) => ipcRenderer.send("shell", type, data),
  invoke: (type, data) => ipcRenderer.invoke("shell-invoke", type, data),
  on: (type, handler) => {
    ipcRenderer.on("shell", (_event, messageType, data) => {
      if (messageType === type) handler(data);
    });
  },
});
