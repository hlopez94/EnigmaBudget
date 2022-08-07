import { Component } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { environment } from 'src/environments/environment';
import { firstValueFrom } from 'rxjs';

export interface ApiResponse<T>{
  ok:boolean;
  result:T;
}

@Component({
  selector: 'app-root',
  templateUrl: './app.component.html',
  styleUrls: ['./app.component.scss'],
})
export class AppComponent {
  title = 'enigma-ui';
  apiResponse : any | null = null;
  constructor(private _http: HttpClient) {}

  callApi() {
   firstValueFrom(this._http.get<ApiResponse<string>>(`${environment.settings.apiUrl}api/base`)).then(res => this.apiResponse=res.result);
  }
}

