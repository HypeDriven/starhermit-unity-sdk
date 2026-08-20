// Browser half of the WebGL socket adapter.
//
// Unity's WebGL player has no managed sockets, so every Starhermit realtime protocol reaches the
// browser's own WebSocket through here. One handle is one connection; whole messages are queued for
// the managed side to drain, which keeps frame reassembly the browser's job rather than the SDK's.
//
// Nothing here logs a URL: the handshake carries the access token in its query string.
mergeInto(LibraryManager.library, {
  $starhermitSockets: {
    next: 1,
    map: {},
  },

  StarhermitSocketCreate__deps: ['$starhermitSockets'],
  StarhermitSocketCreate: function (urlPtr) {
    var url = UTF8ToString(urlPtr);
    var handle = starhermitSockets.next++;
    var entry = { socket: null, queue: [], state: 0, closeCode: 0 };
    starhermitSockets.map[handle] = entry;

    try {
      var socket = new WebSocket(url);
      socket.binaryType = 'arraybuffer';
      entry.socket = socket;

      socket.onopen = function () { entry.state = 1; };
      socket.onmessage = function (event) {
        if (typeof event.data === 'string') {
          var encoded = new TextEncoder().encode(event.data);
          entry.queue.push({ bytes: encoded, isText: 1 });
        } else {
          entry.queue.push({ bytes: new Uint8Array(event.data), isText: 0 });
        }
      };
      socket.onerror = function () { if (entry.state < 2) { entry.state = 3; } };
      socket.onclose = function (event) {
        entry.state = 2;
        entry.closeCode = event && event.code ? event.code : 1006;
      };
    } catch (error) {
      entry.state = 3;
      entry.closeCode = 1006;
    }

    return handle;
  },

  StarhermitSocketState__deps: ['$starhermitSockets'],
  StarhermitSocketState: function (handle) {
    var entry = starhermitSockets.map[handle];
    return entry ? entry.state : 3;
  },

  StarhermitSocketCloseCode__deps: ['$starhermitSockets'],
  StarhermitSocketCloseCode: function (handle) {
    var entry = starhermitSockets.map[handle];
    return entry ? entry.closeCode : 1006;
  },

  StarhermitSocketSend__deps: ['$starhermitSockets'],
  StarhermitSocketSend: function (handle, dataPtr, length, isText) {
    var entry = starhermitSockets.map[handle];
    if (!entry || !entry.socket || entry.socket.readyState !== 1) { return; }

    var bytes = HEAPU8.subarray(dataPtr, dataPtr + length);
    if (isText) {
      entry.socket.send(new TextDecoder('utf-8').decode(bytes));
    } else {
      // Copy: the heap view is only valid until the next allocation, and send() is asynchronous.
      entry.socket.send(new Uint8Array(bytes).buffer);
    }
  },

  StarhermitSocketReceiveLength__deps: ['$starhermitSockets'],
  StarhermitSocketReceiveLength: function (handle) {
    var entry = starhermitSockets.map[handle];
    if (!entry || entry.queue.length === 0) { return 0; }
    return entry.queue[0].bytes.length;
  },

  StarhermitSocketReceive__deps: ['$starhermitSockets'],
  StarhermitSocketReceive: function (handle, bufferPtr, capacity, isTextPtr) {
    var entry = starhermitSockets.map[handle];
    if (!entry || entry.queue.length === 0) { return 0; }

    var message = entry.queue[0];
    if (message.bytes.length > capacity) { return -1; }
    entry.queue.shift();

    HEAPU8.set(message.bytes, bufferPtr);
    HEAP32[isTextPtr >> 2] = message.isText;
    return message.bytes.length;
  },

  StarhermitSocketClose__deps: ['$starhermitSockets'],
  StarhermitSocketClose: function (handle, code, reasonPtr) {
    var entry = starhermitSockets.map[handle];
    if (!entry || !entry.socket) { return; }
    try {
      entry.socket.close(code, UTF8ToString(reasonPtr));
    } catch (error) {
      // A socket that is already closing throws here; the state is what we wanted either way.
    }
  },

  StarhermitSocketDestroy__deps: ['$starhermitSockets'],
  StarhermitSocketDestroy: function (handle) {
    var entry = starhermitSockets.map[handle];
    if (!entry) { return; }
    try {
      if (entry.socket && entry.socket.readyState <= 1) { entry.socket.close(1000, 'disposed'); }
    } catch (error) {
      // Nothing to do: the connection is going away regardless.
    }
    delete starhermitSockets.map[handle];
  },
});
