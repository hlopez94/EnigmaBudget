
export interface Store<T> {
  status: "ok" | "cargando" | "error" | null;
  data: T | null;
  errorText: string | null;
}
