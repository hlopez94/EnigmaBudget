import { EnvironmentDef } from "./environment.interface";

export const environment : EnvironmentDef = {
  production: false,
  settings: { 
    apiUrl: '/api', 
    debug: true
  }  
};