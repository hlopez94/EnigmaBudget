import { Component, OnInit } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';

@Component({
    selector: 'app-unverified-account',
    templateUrl: './unverified-account.component.html',
    styleUrls: ['./unverified-account.component.scss'],
    standalone: true,
    imports: [MatIconModule, MatButtonModule]
})
export class UnverifiedAccountComponent implements OnInit {

  constructor() { }

  ngOnInit(): void {
  }

}
