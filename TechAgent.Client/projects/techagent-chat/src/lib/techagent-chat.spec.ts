import { ComponentFixture, TestBed } from '@angular/core/testing';

import { TechagentChat } from './techagent-chat';

describe('TechagentChat', () => {
  let component: TechagentChat;
  let fixture: ComponentFixture<TechagentChat>;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [TechagentChat]
    })
    .compileComponents();

    fixture = TestBed.createComponent(TechagentChat);
    component = fixture.componentInstance;
    await fixture.whenStable();
  });

  it('should create', () => {
    expect(component).toBeTruthy();
  });
});
