export interface EnvironmentDef{
    production: boolean
    settings: {
        apiUrl: string,
        debug:boolean
    }
}