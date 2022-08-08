import { HttpClient } from '@angular/common/http';
import { Injectable } from '@angular/core';
import { BehaviorSubject, firstValueFrom, Observable } from 'rxjs';
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
  reason:string;
}

export interface LoginRequest {
  userName: string;
  password: string;
}

@Injectable({
  providedIn: 'root',
})
export class AuthService {
  private _isUserLoggedIn: BehaviorSubject<boolean>;

  isUserLoggedInObservable: Observable<boolean>;

  userToken: string | null;
  constructor(private _httpClient: HttpClient) {
    this.userToken = localStorage.getItem('token');

    this._isUserLoggedIn = new BehaviorSubject<boolean>(this.userToken != null);
    this.isUserLoggedInObservable = this._isUserLoggedIn.asObservable();
  }

  isUserLoggedIn(): boolean {
    return this._isUserLoggedIn.value;
  }

  async loginUserWithCredentials(
   request:LoginRequest
  ): Promise<LoginResponse> {
    var res = await firstValueFrom(
      this._httpClient.post<ApiResponse<LoginResponse>>(
        `${environment.settings.apiUrl}/user/login`,
        request
      )
    );

    if (res.result.loggedIn) {
      localStorage.setItem('token', res.result.jwt);
      this.userToken = res.result.jwt;
      this._isUserLoggedIn.next(true);
    } else {
      localStorage.removeItem('token');
      this.userToken = null;
      this._isUserLoggedIn.next(false);
    }

    return res.result;
  }
}
