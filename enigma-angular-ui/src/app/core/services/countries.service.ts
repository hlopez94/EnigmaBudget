import { TypedApiResponse } from 'src/app/core/model/ApiResponse';
import { environment } from 'src/environments/environment';
import { HttpClient } from '@angular/common/http';
import { Injectable } from '@angular/core';
import { Pais } from '../model/pais';
import { firstValueFrom } from 'rxjs';

@Injectable({
  providedIn: 'root'
})
export class CountriesService {

  constructor(private httpClient: HttpClient) { }

  async getAllCountries() : Promise<Pais[]> {
    var countries = await firstValueFrom(
      this.httpClient.get<TypedApiResponse<Pais[]>>(`${environment.settings.apiUrl}/countries`)
    )
    return countries.data;
  }
}
