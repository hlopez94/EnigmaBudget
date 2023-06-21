import { CommonModule } from '@angular/common';
import { Component, OnInit } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatToolbarModule } from '@angular/material/toolbar';
import { AuthDropdownModule } from 'src/app/auth/auth-dropdown.module';
import { AppStateService } from 'src/app/core/services/app-state.service';

@Component({
  selector: 'app-navbar',
  templateUrl: './navbar.component.html',
  styleUrls: ['./navbar.component.scss'],
  standalone:true,
  imports: [
    CommonModule,
    MatButtonModule,
    MatIconModule,
    MatToolbarModule,
    AuthDropdownModule
  ],
  providers:[
    AppStateService
  ]
})
export class NavbarComponent implements OnInit {
  $activeTheme = this._appStateService.activeTheme;
  constructor(private _appStateService: AppStateService) {}

  ngOnInit(): void {}

  toggleSidebar() {
    this._appStateService.toggleSidebar();
  }

  async intercambiarTema(){
    await this._appStateService.toggleTheme();
  }
}
