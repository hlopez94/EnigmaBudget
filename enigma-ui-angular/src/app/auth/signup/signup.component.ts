import { Component } from '@angular/core';
import { FormControl, FormGroup, Validators, ReactiveFormsModule } from '@angular/forms';
import { AuthService } from '../auth.service';
import { MatButtonModule } from '@angular/material/button';
import { MatInputModule } from '@angular/material/input';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatCardModule } from '@angular/material/card';
import { MatSnackBar, MatSnackBarModule } from '@angular/material/snack-bar';
import { Router, RouterModule } from '@angular/router';

@Component({
    selector: 'app-signup',
    templateUrl: './signup.component.html',
    styleUrls: ['./signup.component.scss'],
    standalone: true,
    imports: [
      MatButtonModule,
      MatCardModule,
      MatFormFieldModule,
      MatInputModule,
      MatSnackBarModule,
      ReactiveFormsModule,
      RouterModule,
    ],
})
export class SignupComponent {

  signupForm= new FormGroup({
    userName: new FormControl('', [
      Validators.required,
      Validators.minLength(6),
    ]),
    email: new FormControl('', [
      Validators.required,
      Validators.email
    ]),
    password: new FormControl('', [
      Validators.required,
      Validators.minLength(8),
    ]),
    passwordrepeat: new FormControl('', [
      Validators.required,
      Validators.minLength(8),
    ])
  });

  constructor(private _authService: AuthService, private _snackBar: MatSnackBar, private _router:Router) {
  }


  async signup() {
    try{
    await this._authService.SignUp(this.signupForm.value)
    .then(res =>{
      if(res.isSuccess){

        this._snackBar.open(`Bienvenido ${this.signupForm.value.userName}, ya podes iniciar sesión`, undefined, {
          duration: 3000,
          panelClass: ['mat-primary'],
        });
        
        this._router.navigateByUrl('/auth/login');
      }else 
        this._snackBar.open(res.errorsText,'', { duration: 3000 });

    });
    
  } catch (err: any) {
    switch (err.status) {
      case 0:
        this._snackBar.open('El servidor no responde.', undefined, {
          duration: 3000,
          panelClass: ['mat-toolbar', 'mat-warn'],
        });
        break;
      default:
        this._snackBar.open(err.error.errorsText,'', { duration: 3000 });
    }
  }

  }
}
