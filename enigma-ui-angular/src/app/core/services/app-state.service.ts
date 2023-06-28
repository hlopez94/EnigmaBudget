import { Injectable } from '@angular/core';
import { BehaviorSubject, Observable } from 'rxjs';

export type AppTheme = 'light' | 'dark';

@Injectable({
  providedIn: 'root',
})
export class AppStateService {
  private _isSidebarOpen: BehaviorSubject<boolean>;
  public isSidebarOpen: Observable<boolean>;

  private _activeTheme: BehaviorSubject<AppTheme>;
  public activeTheme: Observable<AppTheme>;
  private _userThemeActive: boolean = false;

  private _mostrarBalances: BehaviorSubject<'mostrar'|'ocultar'>;
  public mostrarBalances: Observable<'mostrar'|'ocultar'>;
  private readonly STORAGE_KEY_MOSTRAR_BALANCE = 'app-mostrar-balance'

  constructor() {
    this._isSidebarOpen = new BehaviorSubject<boolean>(false);
    this.isSidebarOpen = this._isSidebarOpen.asObservable();
    this._activeTheme = new BehaviorSubject<AppTheme>('light');
    this.activeTheme = this._activeTheme.asObservable();

    this._mostrarBalances = new BehaviorSubject<'mostrar'|'ocultar'>('mostrar');
    this.mostrarBalances = this._mostrarBalances.asObservable();

    this.setupMostrarBalance();

    //If user previously set prefered theme apply it
    if ((localStorage.getItem('app-theme') as AppTheme) != null) {
      this._userThemeActive = true;
      this.setThemeSelf(localStorage.getItem('app-theme') as AppTheme);
    } else {
      //Watch for system theme change and apply it
      this.setThemeSelf(
        window.matchMedia('(prefers-color-scheme: dark)').matches
          ? 'dark'
          : 'light'
      );
      window
        .matchMedia('(prefers-color-scheme: dark)')
        .addEventListener('change', (event) => {
          if (!this._userThemeActive)
            this.setThemeSelf(event.matches ? 'dark' : 'light');
        });
    }
  }

  private setupMostrarBalance() {
    var mostrar: 'mostrar' | 'ocultar' | null = localStorage.getItem(this.STORAGE_KEY_MOSTRAR_BALANCE) as 'mostrar' | 'ocultar'  | null;
    if (mostrar == null || mostrar =='mostrar') {
      this._mostrarBalances.next('mostrar');
      localStorage.setItem(this.STORAGE_KEY_MOSTRAR_BALANCE,'mostrar')
    } else this._mostrarBalances.next('ocultar');
  }

  toggleMostrarBalances() {
    if(this._mostrarBalances.value == 'mostrar'){
      localStorage.setItem(this.STORAGE_KEY_MOSTRAR_BALANCE, 'ocultar' );
      this._mostrarBalances.next('ocultar');
    }
      else {
        localStorage.setItem(this.STORAGE_KEY_MOSTRAR_BALANCE, 'mostrar' );
        this._mostrarBalances.next('mostrar');}
  }

  private setThemeSelf(theme: AppTheme) {
    if (theme == 'light' || !theme) this.setLightTheme();
    else this.setDarkTheme();
  }

  public toggleSidebar() {
    this._isSidebarOpen.next(!this._isSidebarOpen.getValue());
  }

  public toggleTheme() {
    if (!this._userThemeActive) this._userThemeActive = true;

    if (this._activeTheme.value == 'light') {
      this.setDarkTheme();
    } else this.setLightTheme();
  }

  private setDarkTheme() {
    if (this._userThemeActive) localStorage.setItem('app-theme', 'dark');

    this._activeTheme.next('dark');
    document.getElementsByTagName('html')[0].classList.add('theme-alternate');
  }

  private setLightTheme() {
    if (this._userThemeActive) localStorage.setItem('app-theme', 'light');

    this._activeTheme.next('light');
    document
      .getElementsByTagName('html')[0]
      .classList.remove('theme-alternate');
  }
}
