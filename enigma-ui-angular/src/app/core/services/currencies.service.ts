import { environment } from 'src/environments/environment';
import { TypedApiResponse } from 'src/app/core/model/ApiResponse';
import { HttpClient } from '@angular/common/http';
import { firstValueFrom } from 'rxjs';
import { Injectable } from '@angular/core';
import { Divisa } from '../model/divisa';

@Injectable({
  providedIn: 'root',
})
export class CurrenciesService {
  constructor(private httpClient: HttpClient) {}

  async ObtenerTodas(): Promise<TypedApiResponse<Divisa[]>> {
    var res = await firstValueFrom(
      this.httpClient.get<TypedApiResponse<Divisa[]>>(
        `${environment.settings.apiUrl}/currencies`
      )
    );
    return res;
  }
}
