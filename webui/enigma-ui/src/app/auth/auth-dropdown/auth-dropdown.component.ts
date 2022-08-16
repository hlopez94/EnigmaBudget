import { Component, OnInit } from '@angular/core';
import { AuthService } from '../auth.service';

@Component({
  selector: 'app-auth-dropdown',
  templateUrl: './auth-dropdown.component.html',
  styleUrls: ['./auth-dropdown.component.scss'],
})
export class AuthDropdownComponent implements OnInit {
  constructor(private _authSvc: AuthService) {}

  ngOnInit(): void {}

  logout() {
    this._authSvc.logout();
    location.reload();
  }
}
