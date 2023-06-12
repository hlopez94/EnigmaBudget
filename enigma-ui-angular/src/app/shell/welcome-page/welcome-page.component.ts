import { Router } from '@angular/router';
import { Subscription } from 'rxjs';
import { AuthService } from './../../auth/auth.service';
import { Component,  OnDestroy } from '@angular/core';

@Component({
  selector: 'app-welcome-page',
  templateUrl: './welcome-page.component.html',
  styleUrls: ['./welcome-page.component.scss'],
})
export class WelcomePageComponent implements OnDestroy {
  private isLoggedInSubscription: Subscription;
  constructor(private authService: AuthService, private router: Router) {
    this.isLoggedInSubscription = this.authService.IsUserLoggedIn.subscribe(
      (isLoggedIn) => {
        if (isLoggedIn) this.router.navigateByUrl('/user-dashboard');
      }
    );
  }

  async ngOnDestroy() {
    this.isLoggedInSubscription.unsubscribe();
  }
}
