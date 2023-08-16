import {
  AfterViewInit,
  Component,
  OnDestroy,
  OnInit,
} from '@angular/core';
import { NavbarComponent } from './shell/navbar/navbar.component';
import { FooterComponent } from './shell/footer/footer.component';
import { RouterModule } from '@angular/router';

@Component({
  selector: 'app-root',
  templateUrl: './app.component.html',
  styleUrls: ['./app.component.scss'],
  standalone: true,
  imports: [
    NavbarComponent,
    FooterComponent,
    RouterModule
  ]
})
export class AppComponent implements OnInit, OnDestroy, AfterViewInit {

  constructor() {}

  ngOnInit() {}

  ngAfterViewInit() {}
  ngOnDestroy(): void {}
}
