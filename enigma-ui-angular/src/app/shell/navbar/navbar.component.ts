import { CommonModule } from '@angular/common';
import { Component, OnInit } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatMenuModule } from '@angular/material/menu';
import { MatToolbarModule } from '@angular/material/toolbar';
import { RouterModule } from '@angular/router';
import { AuthDropdownComponent } from 'src/app/auth/auth-dropdown/auth-dropdown.component';
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
    RouterModule,
    MatMenuModule,
    AuthDropdownComponent
  ]
})
export class NavbarComponent implements OnInit {
  $activeTheme = this._appStateService.activeTheme;
  $mostrarBalances = this._appStateService.mostrarBalances;
  constructor(private _appStateService: AppStateService) {}

  ngOnInit(): void {}

  toggleSidebar() {
    this._appStateService.toggleSidebar();
  }

  async intercambiarTema(){
    await this._appStateService.toggleTheme();
  }

  async intercambiarMostrarBalances(){
    await this._appStateService.toggleMostrarBalances();
  }

}
