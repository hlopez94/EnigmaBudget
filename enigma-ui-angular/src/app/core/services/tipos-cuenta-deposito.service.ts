import { Observable } from 'rxjs';
import { environment } from 'src/environments/environment';
import { TypedApiResponse } from 'src/app/core/model/ApiResponse';
import { HttpClient } from '@angular/common/http';
import { Injectable } from '@angular/core';
import { TipoCuentaDeposito } from '../model/TipoCuentaDeposito';

@Injectable({
  providedIn: 'root',
})
export class CuentasDepositoService {
  constructor(private httpClient: HttpClient) {}

  cargarTiposCuentaDeposito(): Observable<TypedApiResponse<TipoCuentaDeposito[]>> {
    return this.httpClient.get<TypedApiResponse<TipoCuentaDeposito[]>>(
      `${environment.settings.apiUrl}/DepositAccountTypes`
    );
  }
  cargarTipoCuentaDepositoPorId(id:string): Observable<TypedApiResponse<TipoCuentaDeposito>> {
    return this.httpClient.get<TypedApiResponse<TipoCuentaDeposito>>(
      `${environment.settings.apiUrl}/DepositAccountTypes/${id}`
    );
  }

  cargarTiposCuentaDepositoPorEnum(enumString:string) : Observable<TypedApiResponse<TipoCuentaDeposito>> {
    return this.httpClient.get<TypedApiResponse<TipoCuentaDeposito>>(
      `${environment.settings.apiUrl}/DepositAccountTypes/enum/${enumString}`
    );
  }
}
