import { Injectable } from '@angular/core';
import { BehaviorSubject, Observable } from 'rxjs';
import { __exportStar } from 'tslib';

export type AppTheme = 'light' | 'dark';

@Injectable({
  providedIn: 'root',
})
export class AppStateService {
  private _isSidebarOpen: BehaviorSubject<boolean>;
  public isSidebarOpen: Observable<boolean>;

  private _activeTheme: BehaviorSubject<AppTheme>;
  public activeTheme: Observable<AppTheme>;

  constructor() {
    this._isSidebarOpen = new BehaviorSubject<boolean>(false);
    this.isSidebarOpen = this._isSidebarOpen.asObservable();

    let activeTheme: AppTheme =
      (localStorage.getItem('app-theme') as AppTheme) ?? 'light';

    this._activeTheme = new BehaviorSubject<AppTheme>(activeTheme);
    this.activeTheme = this._activeTheme.asObservable();
  }

  public toggleSidebar() {
    this._isSidebarOpen.next(!this._isSidebarOpen.getValue());
  }

  public toggleTheme() {
    if (this._activeTheme.value == 'light') {
      this.setDarkTheme();
    } else this.setLightTheme();
  }

  private setDarkTheme() {
    localStorage.setItem('app-theme', 'dark');
    this._activeTheme.next('dark');
  }

  private setLightTheme() {
    localStorage.setItem('app-theme', 'light');
    this._activeTheme.next('light');
  }
}
