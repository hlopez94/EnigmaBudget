import { Component, OnInit } from '@angular/core';
import { FormControl, FormGroup, Validators } from '@angular/forms';
import { AuthService, LoginRequest } from '../auth.service';
import { MatSnackBar } from '@angular/material/snack-bar';
import { ActivatedRoute, Params } from '@angular/router';
@Component({
  selector: 'app-login',
  templateUrl: './login.component.html',
  styleUrls: ['./login.component.scss'],
})
export class LoginComponent implements OnInit {
  loginForm: FormGroup;
  originUrl: string | undefined;

  constructor(
    private _authService: AuthService,
    private _snackBar: MatSnackBar,
    private _route: ActivatedRoute
  ) {
    this.loginForm = new FormGroup({
      userName: new FormControl('', [
        Validators.required,
        Validators.minLength(6),
      ]),
      password: new FormControl('', [
        Validators.required,
        Validators.minLength(8),
      ]),
    });
  }

  ngOnInit(): void {
    this._route.queryParams.subscribe((params: Params) => {
      this.originUrl = params['originUrl'];
    });
  }

  async login() {
    var resLogin = await this._authService.loginUserWithCredentials(
      this.loginForm.value as LoginRequest
    );
    if (resLogin.loggedIn) {
    } else {
    }
  }
}
