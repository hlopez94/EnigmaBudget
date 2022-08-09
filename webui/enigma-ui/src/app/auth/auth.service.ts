import { HttpClient } from '@angular/common/http';
import { Injectable, OnDestroy } from '@angular/core';
import { BehaviorSubject, firstValueFrom, Observable, takeWhile, tap } from 'rxjs';
import { environment } from 'src/environments/environment';

export interface ApiResponse<T> {
  ok: boolean;
  result: T;
}

export interface LoginResponse {
  loggedIn: boolean;
  jwt: string;
  userName: string;
  email: string;
  reason: string;
}

export interface LoginRequest {
  userName: string;
  password: string;
}

@Injectable({
  providedIn: 'root',
})
export class AuthService {

  private _IsUserLoggedIn: BehaviorSubject<boolean>;
  public IsUserLoggedIn: Observable<boolean>;

  isUserLoggedInSync(): boolean {
    return this._IsUserLoggedIn.value;
  }

  constructor(private _httpClient: HttpClient) {
    this._IsUserLoggedIn = new BehaviorSubject<boolean>(false);
    this.IsUserLoggedIn = this._IsUserLoggedIn.asObservable();

    var token = localStorage.getItem('token');
    if (token) {
      this.setearToken(token);
    }
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

    if (res.result.loggedIn) {
      this.setearToken(res.result.jwt);
    } else {
      this.limpiarToken();
    }

    return res.result;
  }

  public logout() {
    this.limpiarToken();
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

  private programarVencimientoToken(token: string) {

  }

  private tokenExpirationDate(token: string): Date {
    const expiryInSeconds = JSON.parse(atob(token.split('.')[1])).exp;
    return new Date(expiryInSeconds * 1000);
  }
  private tokenExpired(token: string): boolean {
    const expiryInSeconds = JSON.parse(atob(token.split('.')[1])).exp;
    return Math.floor(new Date().getTime() / 1000) >= expiryInSeconds;
  }
}
