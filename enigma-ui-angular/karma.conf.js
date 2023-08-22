process.env.CHROME_BIN = require("puppeteer").executablePath();

module.exports = function (config) {
  config.set({
    basePath: '',
    frameworks: ['jasmine', '@angular-devkit/build-angular'],
    plugins: [
      require('karma-jasmine'),
      require('karma-chrome-launcher'),
      require('karma-jasmine-html-reporter'),
      require('karma-coverage'),
      require('@angular-devkit/build-angular/plugins/karma'),
      require('karma-firefox-launcher'),
      require('@chiragrupani/karma-chromium-edge-launcher'),
    ],
    client: {
      jasmine: { },
      clearContext: false // leave Jasmine Spec Runner output visible in browser
    },
    jasmineHtmlReporter: {
      suppressAll: true // removes the duplicated traces
    },
    coverageReporter: {
      dir: require('path').join(__dirname, './coverage/enigma-ui'),
      subdir: '.',
      reporters: [
        {type: 'html', subdir: 'html-coverage'},
        {type: 'lcovonly'},
        {type: 'text-summary'},
        {type: 'json-summary'},
      ],
      fixWebpackSourcePaths: true
    },
    reporters: ['progress', 'kjhtml'],
    port: 9876,
    colors: true,
    logLevel: config.LOG_INFO,
    autoWatch: true,
    browsers: ['Chrome','Edge','Firefox', 'ChromeHeadlessCI'],
    customLaunchers: {
      ChromeHeadlessCI: {
          base: 'ChromeHeadless',
          flags: [
            '--no-sandbox',
            '--disable-gpu',
          ],
      }
    },
    singleRun: false,
    restartOnFileChange: true    
  });
};
