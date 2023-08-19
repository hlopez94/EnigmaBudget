import { HttpClient } from '@angular/common/http';
import { Injectable } from '@angular/core';
import { BehaviorSubject, firstValueFrom, Observable} from 'rxjs';
import { environment } from 'src/environments/environment';
import { TypedApiResponse } from '../core/model/ApiResponse';
import { LoginRequest } from './login/login-request';
import { LoginResponse } from './model/login-response';
import { Profile } from './model/profile';

@Injectable({
  providedIn: 'root',
})
export class AuthService {
  private _isUserLoggedIn: BehaviorSubject<boolean>;
  public $isUserLoggedIn: Observable<boolean>;

  constructor(private _httpClient: HttpClient) {
    this._isUserLoggedIn = new BehaviorSubject<boolean>(false);
    this.$isUserLoggedIn = this._isUserLoggedIn.asObservable();

    var token = localStorage.getItem('token');
    if (token) {
      this.setearToken(token);
    }
  }

  async verifyAccountMail(verifyEmailToken: string): Promise<TypedApiResponse<boolean>>  {
    var uri = `${environment.settings.apiUrl}/user/verify-email-account`
    console.log(uri);
    return await firstValueFrom(this._httpClient.post<TypedApiResponse<boolean>>(uri, verifyEmailToken));
  }

  async getProfile(): Promise<Profile> {
    var res = await firstValueFrom(
      this._httpClient.get<TypedApiResponse<Profile>>(
        `${environment.settings.apiUrl}/user/profile`
      )
    );

    if(res.data.fechaNacimiento)
      res.data.fechaNacimiento = new Date(Date.parse(res.data.fechaNacimiento.toString()));
      else res.data.fechaNacimiento=null;

    return res.data;
  }

  async updateProfile(perfil: Profile): Promise<boolean> {
    var res = await firstValueFrom(
      this._httpClient.put<TypedApiResponse<boolean>>(
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

  public isUserLoggedIn() : boolean{
    return localStorage.getItem('token') != null;
  }
  private limpiarToken() {
    localStorage.removeItem('token');
    this._isUserLoggedIn.next(false);
  }

  private setearToken(token: string) {
    if (!this.tokenExpired(token)) {
      localStorage.setItem('token', token);
      this._isUserLoggedIn.next(true);
      this.programarVencimientoToken(token);
    } else {
      this.limpiarToken();
    }
  }

  private programarVencimientoToken(token: string) {}

  private tokenExpired(token: string): boolean {
    if(token && token.split('.')[1] ){
      const expiryInSeconds = JSON.parse(atob(token.split('.')[1])).exp;
      return Math.floor(new Date().getTime() / 1000) >= expiryInSeconds;
    }
    return false;
  }


  public cuentaVerificada() : Boolean{
    var token = localStorage.getItem('token');

    if(token && token.split('.')[1]){
      var parse = JSON.parse(atob(token.split('.')[1]));
      var verificado : Boolean = parse['verified-account'] as Boolean;
      return verificado;
    }

    return false;
  }

  public async SignUp(signupInfo: any) : Promise<TypedApiResponse<any>>{
    return await firstValueFrom(this._httpClient.post<TypedApiResponse<any>>(`${environment.settings.apiUrl}/user/signup`, signupInfo))
 }
}
