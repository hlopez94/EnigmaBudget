import { Component, OnInit } from '@angular/core';
import { FormControl, FormGroup, Validators } from '@angular/forms';
import { AuthService } from '../auth.service';
import { MatSnackBar } from '@angular/material/snack-bar';
import { ActivatedRoute, Params, Router } from '@angular/router';
import { MAT_FORM_FIELD_DEFAULT_OPTIONS } from '@angular/material/form-field';
import { LoginRequest } from './login-request';

@Component({
  selector: 'app-login',
  templateUrl: './login.component.html',
  providers: [
    {
      provide: MAT_FORM_FIELD_DEFAULT_OPTIONS,
      useValue: { appearance: 'outline' },
    },
  ],
  styleUrls: ['./login.component.scss'],
})
export class LoginComponent implements OnInit {
  loginForm: FormGroup;

  originUrl: string[] | string | null = null;
  originParams: Params | null = null;

  constructor(
    private _authService: AuthService,
    private _route: ActivatedRoute,
    private _router: Router,
    private _snackBar: MatSnackBar
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
    if (this._authService.isUserLoggedInSync()) {
      this._router.navigate(['profile']);
    }

    this._route.queryParams.subscribe((params: Params) => {
      this.originUrl = params['origin'] ?? '/';
      if (params['originParams'])
        this.originParams = JSON.parse(params['originParams']);
    });
  }

  async login() {
    try {
      var resLogin = await this._authService.loginUserWithCredentials(
        this.loginForm.value as LoginRequest
      );
      this._snackBar.open(`Bienvenido ${resLogin.userName}`, undefined, {
        duration: 3000,
      });

     var urlPaths : string[]= [];
     urlPaths.concat(this.originUrl!);

      if (this.originUrl && this.originUrl.length > 0)
        this._router.navigate(
           urlPaths.concat(this.originUrl!),
          {
            queryParams: this.originParams,
            queryParamsHandling: '',
          }
        );
      else this._router.navigate(['/']);
    } catch (err: any) {
      console.log(err);
      console.log(this.originUrl);
      this._snackBar.open(err.message, undefined, { duration: 3000 });
    }
  }
}
