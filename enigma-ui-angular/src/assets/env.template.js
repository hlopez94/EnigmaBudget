(function(window) {
    window.env = window.env || {};
  
    // Environment variables
    window["env"]["apiUrl"] = "${APP_API_BASE_URL}";
    window["env"]["debug"] = "${DEBUG}";
  })(this);