import { AfterViewInit, Component, OnDestroy, OnInit, ViewChild } from '@angular/core';
import { MatSidenav } from '@angular/material/sidenav';
import { Subscription } from 'rxjs';
import { AppStateService } from './core/services/app-state.service';

@Component({
  selector: 'app-root',
  templateUrl: './app.component.html',
  styleUrls: ['./app.component.scss'],
})
export class AppComponent implements OnInit, OnDestroy , AfterViewInit{
  private sidebarStateSubscription!: Subscription;

  @ViewChild('sidebarDrawer')
  sidebarDrawer!: MatSidenav;

  constructor(private _appStateService: AppStateService) {}

  ngOnInit() {

  }

  ngAfterViewInit() {
    this.sidebarStateSubscription =
      this._appStateService.isSidebarOpen.subscribe((newState) => {
        if (newState) this.sidebarDrawer.open();
        else this.sidebarDrawer.close();
      });
  }
  ngOnDestroy(): void {
    this.sidebarStateSubscription.unsubscribe();
  }
}
