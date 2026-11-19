mergeInto(LibraryManager.library, {
  GuochaoCampaignWrite: function(keyPtr, valuePtr) {
    try {
      var key = UTF8ToString(keyPtr), value = UTF8ToString(valuePtr);
      window.localStorage.setItem(key, value);
      return window.localStorage.getItem(key) === value ? 1 : 0;
    } catch (error) { return 0; }
  },
  GuochaoCampaignRead: function(keyPtr) {
    var value = "";
    try { value = window.localStorage.getItem(UTF8ToString(keyPtr)) || ""; } catch (error) {}
    var bytes = lengthBytesUTF8(value) + 1;
    var pointer = _malloc(bytes);
    stringToUTF8(value, pointer, bytes);
    return pointer;
  }
});
