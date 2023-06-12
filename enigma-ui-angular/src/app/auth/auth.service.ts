import { HttpClient } from '@angular/common/http';
import { Injectable } from '@angular/core';
import {
  BehaviorSubject,
  firstValueFrom,
  Observable
} from 'rxjs';
import { environment } from 'src/environments/environment';
import {  TypedApiResponse } from '../core/model/ApiResponse';
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

  async verifyAccountMail(token: string): Promise<TypedApiResponse<boolean>>  {
    return await firstValueFrom(this._httpClient.post<TypedApiResponse<boolean>>(`${environment.settings.apiUrl}/user/verify-email-account`, token))

  }

  async getProfile(): Promise<Profile> {
    var res = await firstValueFrom(
      this._httpClient.get<TypedApiResponse<Profile>>(
        `${environment.settings.apiUrl}/user/profile`
      )
    );

    if(res.data.fechaNacimiento )
      res.data.fechaNacimiento = new Date(Date.parse(res.data.fechaNacimiento.toString()));
      else res.data.fechaNacimiento=null;

    return res.data;
  }

  async updateProfile(perfil: Profile): Promise<boolean> {
    var res = await firstValueFrom(
      this._httpClient.post<TypedApiResponse<boolean>>(
        `${environment.settings.apiUrl}/user/profile`,
        perfil
      )
    );

    return res.data;
  }

  async loginUserWithCredentials(
    request: LoginRequest
  ): Promise<LoginResponse> {
    var res = await firstValueFrom(
      this._httpClient.post<TypedApiResponse<LoginResponse>>(
        `${environment.settings.apiUrl}/user/login`,
        request
      )
    );

    if (res.isSuccess) {
      this.setearToken(res.data.jwt);
    } else {
      this.limpiarToken();
      throw Error(res.errorText);
    }

    return res.data;
  }

  public logout() {
    this.limpiarToken();
  }

  public async paises() : Promise<Pais[]>{
    var res = await firstValueFrom(
      this._httpClient.get<TypedApiResponse<Pais[]>>(
        `${environment.settings.apiUrl}/user/countries`
      )
    );

    return res.data;
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

  public async SignUp(signupInfo: any) : Promise<TypedApiResponse<any>>{
    return await firstValueFrom(this._httpClient.post<TypedApiResponse<any>>(`${environment.settings.apiUrl}/user/signup`, signupInfo))
 }
}
