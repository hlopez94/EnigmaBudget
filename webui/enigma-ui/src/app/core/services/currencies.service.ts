import { environment } from 'src/environments/environment';
import { ApiResponse } from 'src/app/core/model/ApiResponse';
import { HttpClient } from '@angular/common/http';
import { firstValueFrom } from 'rxjs';
import { Injectable } from '@angular/core';
import { Moneda } from '../model/moneda';

@Injectable({
  providedIn: 'root',
})
export class CurrenciesService {
  constructor(private httpClient: HttpClient) {}

  async ObtenerTodas(): Promise<Moneda[]> {
    var res = await firstValueFrom(
      this.httpClient.get<ApiResponse<Moneda[]>>(
        `${environment.settings.apiUrl}/currencies`
      )
    );
    return res.data;
  }
}
