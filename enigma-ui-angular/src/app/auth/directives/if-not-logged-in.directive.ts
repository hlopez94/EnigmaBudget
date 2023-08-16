import {
  Directive,
  ElementRef,
  OnDestroy,
  OnInit
} from '@angular/core';
import { Subscription } from 'rxjs';
import { AuthService } from '../auth.service';

@Directive({
  selector: '[ifNotLoggedIn]',
  standalone:true,
  providers: [
    AuthService
  ]
})
export class IfNotLoggedInDirective implements OnInit, OnDestroy {
  susbscription: Subscription | undefined;

  constructor(
    private elementRef: ElementRef<any>,
    private authSvc: AuthService
  ) {}

  ngOnInit(): void {
    this.susbscription = this.authSvc.$isUserLoggedIn.subscribe((loggedIn) => {
      this.elementRef.nativeElement.style.display = loggedIn ? 'none' : null;
    });
  }

  ngOnDestroy(): void {
    this.susbscription?.unsubscribe();
  }
}
