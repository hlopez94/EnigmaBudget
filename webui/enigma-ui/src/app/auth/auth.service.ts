import { HttpClient } from '@angular/common/http';
import { Injectable, OnDestroy } from '@angular/core';
import {
  BehaviorSubject,
  firstValueFrom,
  Observable
} from 'rxjs';
import { environment } from 'src/environments/environment';
import { ApiResponse } from '../core/model/ApiResponse';
import { LoginRequest } from './login/login-request';
import { LoginResponse } from './model/login-response';
import { Pais } from './model/pais';
import { Profile } from './model/profile';

@Injectable({
  providedIn: 'root',
})
export class AuthService {
  private _IsUserLoggedIn: BehaviorSubject<boolean>;
  public IsUserLoggedIn: Observable<boolean>;


  constructor(private _httpClient: HttpClient) {
    this._IsUserLoggedIn = new BehaviorSubject<boolean>(false);
    this.IsUserLoggedIn = this._IsUserLoggedIn.asObservable();

    var token = localStorage.getItem('token');
    if (token) {
      this.setearToken(token);
    }
  }

  isUserLoggedInSync(): boolean {
    return this._IsUserLoggedIn.value;
  }

  async verifyAccountMail(token: string): Promise<ApiResponse<boolean>>  {
    return await firstValueFrom(this._httpClient.post<ApiResponse<boolean>>(`${environment.settings.apiUrl}/user/verify-email-account`, token))

  }

  async getProfile(): Promise<Profile> {
    var res = await firstValueFrom(
      this._httpClient.get<ApiResponse<Profile>>(
        `${environment.settings.apiUrl}/user/profile`
      )
    );

    if(res.result.fechaNacimiento )
      res.result.fechaNacimiento = new Date(Date.parse(res.result.fechaNacimiento.toString()));
      else res.result.fechaNacimiento=null;

    return res.result;
  }

  async updateProfile(perfil: Profile): Promise<boolean> {
    var res = await firstValueFrom(
      this._httpClient.post<ApiResponse<boolean>>(
        `${environment.settings.apiUrl}/user/profile`,
        perfil
      )
    );

    return res.result;
  }

  async loginUserWithCredentials(
    request: LoginRequest
  ): Promise<LoginResponse> {
    var res = await firstValueFrom(
      this._httpClient.post<ApiResponse<LoginResponse>>(
        `${environment.settings.apiUrl}/user/login`,
        request
      )
    );

    if (res.ok) {
      this.setearToken(res.result.jwt);
    } else {
      this.limpiarToken();
      throw Error(res.errorText);
    }

    return res.result;
  }

  public logout() {
    this.limpiarToken();
  }

  public async paises() : Promise<Pais[]>{
    var res = await firstValueFrom(
      this._httpClient.get<ApiResponse<Pais[]>>(
        `${environment.settings.apiUrl}/user/countries`
      )
    );

    return res.result;
  }
  private limpiarToken() {
    localStorage.removeItem('token');
    this._IsUserLoggedIn.next(false);
  }

  private setearToken(token: string) {
    if (!this.tokenExpired(token)) {
      localStorage.setItem('token', token);
      this._IsUserLoggedIn.next(true);
      this.programarVencimientoToken(token);
    } else {
      this.limpiarToken();
    }
  }

  private programarVencimientoToken(token: string) {}

  private tokenExpirationDate(token: string): Date {
    const expiryInSeconds = JSON.parse(atob(token.split('.')[1])).exp;
    return new Date(expiryInSeconds * 1000);
  }
  private tokenExpired(token: string): boolean {
    const expiryInSeconds = JSON.parse(atob(token.split('.')[1])).exp;
    return Math.floor(new Date().getTime() / 1000) >= expiryInSeconds;
  }

  public cuentaVerificada() : Boolean{
    var token = localStorage.getItem('token');

    if(token){
      var parse = JSON.parse(atob(token.split('.')[1]));
      console.log(parse);
      var verificado : Boolean = parse['verified-account'] as Boolean;
      console.log(verificado);
      return verificado;
    }

    return false;
  }
}
