// Per-session message board: browser-side SignalR connection so the Identity
// cookie authenticates the hub naturally. The Blazor component registers a
// DotNetObjectReference to receive messages and connection-state changes.
window.sessionBoard = {
    connection: null,

    async connect(sessionId, dotnetRef) {
        await this.disconnect();

        const conn = new signalR.HubConnectionBuilder()
            .withUrl("/hubs/session-board")
            .withAutomaticReconnect()
            .build();

        conn.on("MessagePosted", m => dotnetRef.invokeMethodAsync("OnMessagePosted", m));
        conn.onreconnecting(() => dotnetRef.invokeMethodAsync("OnLiveState", false));
        conn.onreconnected(async () => {
            await conn.invoke("JoinSession", sessionId);
            dotnetRef.invokeMethodAsync("OnLiveState", true);
        });
        conn.onclose(() => dotnetRef.invokeMethodAsync("OnLiveState", false));

        await conn.start();
        await conn.invoke("JoinSession", sessionId);
        this.connection = conn;
        return true;
    },

    async disconnect() {
        if (this.connection) {
            const c = this.connection;
            this.connection = null;
            try { await c.stop(); } catch { /* already gone */ }
        }
    },

    scrollToEnd(el) {
        if (el) el.scrollTop = el.scrollHeight;
    }
};
