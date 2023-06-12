import { Component, OnInit } from '@angular/core';
import { FormControl, FormGroup, Validators } from '@angular/forms';
import { AuthService } from '../auth.service';

@Component({
  selector: 'app-signup',
  templateUrl: './signup.component.html',
  styleUrls: ['./signup.component.scss'],
})
export class SignupComponent implements OnInit {
  signup() {
    this._authService.SignUp(this.signupForm.value);
  }

  signupForm: any;
  constructor(private _authService: AuthService) {
    this.signupForm = new FormGroup({
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
  });}

  ngOnInit(): void {}
}