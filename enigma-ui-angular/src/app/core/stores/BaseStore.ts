import { signal } from "@angular/core";
import { TypedApiResponse } from "../model/ApiResponse";
import { Store } from "./Store";


export class BaseStore<T> {

  protected $store = signal<Store<T>>({ data: null, errorText: null, status: null });
  public _store = this.$store.asReadonly();

  protected setCargando() {
    this.$store.mutate(store => store.status = 'cargando');
  }

  protected setApiLista(apiResponse: TypedApiResponse<T>) {
    if (apiResponse.isSuccess) {
      this.$store.mutate(store => { store.status = 'ok', store.data = apiResponse.data; });
    } else {
      this.$store.mutate(store => { store.status = 'error', store.errorText = apiResponse.errorsText; });
      console.error(apiResponse.errors);
    }
  }

  protected setData(data: T) {
    this.$store.mutate(store => { store.status = 'ok', store.data = data; });
  }

  protected setError(err: Error) {
    this.$store.mutate(store => { store.status = 'error', store.errorText = err.message; });
    console.error(err);
  }

  protected async handleApiRequest(promise: Promise<TypedApiResponse<T>>) {
    this.setCargando();
    try {
      const result = await promise;
      this.setApiLista(result);
    } catch (err: any) { this.setError(err); };
  }
}
