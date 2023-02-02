# EnigmaUi

This project was generated with [Angular CLI](https://github.com/angular/angular-cli) version 13.3.7.

## Build

Run `ng build` to build the project. The build artifacts will be stored in the `dist/` directory.

## Running unit tests

Run `ng test` to execute the unit tests via [Karma](https://karma-runner.github.io).

## Running end-to-end tests

Run `ng e2e` to execute the end-to-end tests via a platform of your choice. To use this command, you need to first add a package that implements end-to-end testing capabilities.

## Further help

To get more help on the Angular CLI use `ng help` or go check out the [Angular CLI Overview and Command Reference](https://angular.io/cli) page.

    
## Serve app with SSL
To be able to test PWA features, you'll need to serve your app through a SSL connection. To do that, add your self-signed certificates which you must generate depending on your host operating system, on the folder *.\ssl*  named as follow:
  - **cert.pem** with your public key 
  - **cert.pem** with your private key
  
  Then serve your app with the command `ng serve --ssl` 

  > On some systems you'll need to add your certificate as trusted for your web browsers to accept connections on the site

## Credits
- [Angular](https://github.com/angular/angular): 
    The modern web developer’s platform.
- [Flag-Icons](https://github.com/lipis/flag-icons): 
    A curated collection of all country flags in SVG — plus the CSS for easier integration.
