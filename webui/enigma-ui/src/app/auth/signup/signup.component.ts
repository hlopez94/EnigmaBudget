import { Component, OnInit } from '@angular/core';
import { FormControl, FormGroup, Validators } from '@angular/forms';

@Component({
  selector: 'app-signup',
  templateUrl: './signup.component.html',
  styleUrls: ['./signup.component.scss'],
})
export class SignupComponent implements OnInit {
  signup() {}

  signupForm: any;
  constructor() {
    this.signupForm = new FormGroup({
    userName: new FormControl('', [
      Validators.required,
      Validators.minLength(6),
    ]),
    correo: new FormControl('', [
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
