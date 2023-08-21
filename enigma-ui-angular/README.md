# EnigmaUi

Proyecto de front end para aplicación EnigmaBudget realizado en Angular

## Code Coverage
| Statements                  | Branches                | Functions                 | Lines             |
| --------------------------- | ----------------------- | ------------------------- | ----------------- |
| ![Statements](https://img.shields.io/badge/statements-56.87%25-red.svg?style=flat) | ![Branches](https://img.shields.io/badge/branches-28.57%25-red.svg?style=flat) | ![Functions](https://img.shields.io/badge/functions-50%25-red.svg?style=flat) | ![Lines](https://img.shields.io/badge/lines-57.65%25-red.svg?style=flat) |
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
  - **key.pem** with your private key
  
  Then serve your app with the command `ng serve --ssl` 

  > On some systems you'll need to add your certificate as trusted for your web browsers to accept connections on the site

## Credits
- [Angular](https://github.com/angular/angular): 
    The modern web developer’s platform.
- [Angular Material](https://github.com/angular/components): 
    Material Design UI components for Angular applications.
- [Flag-Icons](https://github.com/lipis/flag-icons): 
    A curated collection of all country flags in SVG — plus the CSS for easier integration.
- [Istanbul Badges Readme](https://github.com/the-bugging/istanbul-badges-readme):
    Creates and updates README testing coverage badges with your json-summary.