import { Component, OnInit } from '@angular/core';
import { AppStateService } from 'src/app/core/services/app-state.service';

@Component({
  selector: 'app-navbar',
  templateUrl: './navbar.component.html',
  styleUrls: ['./navbar.component.scss'],
})
export class NavbarComponent implements OnInit {
  constructor(private _appStateService: AppStateService) {}

  ngOnInit(): void {}

  toggleSidebar() {
    this._appStateService.toggleSidebar();
  }
}
