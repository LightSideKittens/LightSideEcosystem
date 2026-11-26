mergeInto(LibraryManager.library, {

  Showcase_Emit: function(eventPtr, payloadPtr) {
    var bridge = window.__lsDemoBridge;
    if (bridge) bridge.emit(UTF8ToString(eventPtr), UTF8ToString(payloadPtr));
  }

});
