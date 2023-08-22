import { EnvironmentDef } from "./environment.interface";

export const environment : EnvironmentDef = {
  production: true,
  settings: { 
    apiUrl: (window as any)["env"]["apiUrl"] as string || "default",
    debug: (window as any)["env"]["debug"] as boolean || false
  }  
};
