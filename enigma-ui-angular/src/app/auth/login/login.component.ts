import { Component, OnInit } from '@angular/core';
import {
  FormControl,
  FormGroup,
  Validators,
  ReactiveFormsModule,
} from '@angular/forms';
import { AuthService } from '../auth.service';
import { MatSnackBar, MatSnackBarModule } from '@angular/material/snack-bar';
import { ActivatedRoute, Params, Router, RouterModule } from '@angular/router';
import {
  MAT_FORM_FIELD_DEFAULT_OPTIONS,
  MatFormFieldModule,
} from '@angular/material/form-field';
import { LoginRequest } from './login-request';
import { MatButtonModule } from '@angular/material/button';
import { MatInputModule } from '@angular/material/input';
import { MatCardModule } from '@angular/material/card';
import { CommonModule } from '@angular/common';
import { HttpClientModule } from '@angular/common/http';

@Component({
  selector: 'app-login',
  templateUrl: './login.component.html',
  providers: [
    {
      provide: MAT_FORM_FIELD_DEFAULT_OPTIONS,
      useValue: { appearance: 'outline' },
    },
    AuthService,
  ],
  styleUrls: ['./login.component.scss'],
  standalone: true,
  imports: [
    CommonModule,
    HttpClientModule,
    ReactiveFormsModule,
    MatCardModule,
    MatFormFieldModule,
    MatSnackBarModule,
    MatInputModule,
    MatButtonModule,
    RouterModule,
  ],
})
export class LoginComponent implements OnInit {
  loginForm: FormGroup;

  originUrl: string[] | null = null;
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
    var queryParams = this._route.snapshot.queryParams;
    if (queryParams['originParams'] && queryParams['originParams'] != '{}')
      this.originParams = JSON.parse(queryParams['originParams']);
    if (queryParams['origin']) this.originUrl = queryParams['origin'];
    else this.originUrl = [''];

    this._authService.$isUserLoggedIn.subscribe((isLoggedIn) => {
      if (isLoggedIn)
        this.navigateAfterLogin();
    });
  }

  navigateAfterLogin() {
    //if (this.originUrl == null || this.originUrl.length == 0)
    this._router.navigate(['/'], { relativeTo: null });
    //else this._router.navigate(this.originUrl, {queryParams: this.originParams} )
  }

  async login() {
    try {
      var resLogin = await this._authService.loginUserWithCredentials(
        this.loginForm.value as LoginRequest
      );
      this._snackBar.open(`Bienvenido ${resLogin.userName}`, undefined, {
        duration: 3000,
        panelClass: ['mat-primary'],
      });
    } catch (err: any) {
      console.log(err);
      switch (err.status) {
        case 0:
          this._snackBar.open('El servidor no responde.', undefined, {
            duration: 3000,
            panelClass: ['mat-toolbar', 'mat-warn'],
          });
          break;
        default:
          this._snackBar.open('error', undefined, { duration: 3000 });
      }
    }
  }
}
